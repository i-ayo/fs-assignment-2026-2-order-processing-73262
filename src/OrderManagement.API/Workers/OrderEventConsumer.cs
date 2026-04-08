using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Data;
using OrderManagement.API.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;
using Shared.Domain.Enums;
using Shared.Domain.Events;

namespace OrderManagement.API.Workers;

/// <summary>
/// Closes the event loop: listens to the 5 result exchanges published by the
/// worker microservices and transitions Order status in the database.
///
/// Without this consumer orders would be stuck at InventoryPending forever
/// because the workers only publish to RabbitMQ – they do NOT call the API's
/// HTTP endpoints directly.
///
/// Exchange → handler mapping:
///   inventory.confirmed  → InventoryPending  → InventoryConfirmed → PaymentPending
///   inventory.failed     → InventoryPending  → InventoryFailed
///   payment.approved     → PaymentPending    → PaymentApproved   → ShippingPending
///   payment.failed       → PaymentPending    → PaymentFailed
///   shipping.created     → ShippingPending   → ShippingCreated   → Completed
///
/// Crashes loudly (throws) if RabbitMQ credentials are missing — no silent no-op.
/// </summary>
public class OrderEventConsumer(
    IConfiguration config,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderEventConsumer> logger) : BackgroundService
{
    private IConnection? _connection;
    private IChannel?    _channel;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OrderEventConsumer starting — connecting to CloudAMQP");

        // Throws InvalidOperationException if Host/Username/Password/VirtualHost absent.
        // That crash is intentional: a mis-configured consumer must be visible, not silent.
        var factory = RabbitMqConnectionFactory.Create(config);

        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel    = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        // Subscribe to all 5 result exchanges in parallel
        await BindAndConsumeAsync(
            exchange:  "inventory.confirmed",
            queue:     "api.inventory.confirmed",
            handler:   HandleInventoryConfirmedAsync,
            stoppingToken);

        await BindAndConsumeAsync(
            exchange:  "inventory.failed",
            queue:     "api.inventory.failed",
            handler:   HandleInventoryFailedAsync,
            stoppingToken);

        await BindAndConsumeAsync(
            exchange:  "payment.approved",
            queue:     "api.payment.approved",
            handler:   HandlePaymentApprovedAsync,
            stoppingToken);

        await BindAndConsumeAsync(
            exchange:  "payment.failed",
            queue:     "api.payment.failed",
            handler:   HandlePaymentFailedAsync,
            stoppingToken);

        await BindAndConsumeAsync(
            exchange:  "shipping.created",
            queue:     "api.shipping.created",
            handler:   HandleShippingCreatedAsync,
            stoppingToken);

        logger.LogInformation("OrderEventConsumer listening on 5 exchanges");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        logger.LogInformation("OrderEventConsumer stopping");
        if (_channel is not null)    await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task BindAndConsumeAsync(
        string exchange,
        string queue,
        Func<string, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        await _channel!.ExchangeDeclareAsync(exchange, ExchangeType.Fanout,
            durable: true, cancellationToken: ct);
        await _channel.QueueDeclareAsync(queue, durable: true,
            exclusive: false, autoDelete: false, cancellationToken: ct);
        await _channel.QueueBindAsync(queue, exchange,
            routingKey: string.Empty, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            try
            {
                await handler(body, ct);
                await _channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error handling message from {Exchange}. Nacking. Body: {Body}",
                    exchange, body);
                await _channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false, ct);
            }
        };

        await _channel.BasicConsumeAsync(queue,
            autoAck: false, consumer: consumer, ct);
    }

    /// <summary>Creates a short-lived scope to obtain a scoped OrderDbContext.</summary>
    private OrderDbContext CreateDbContext()
    {
        var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    /// <summary>
    /// inventory.confirmed:
    ///   InventoryPending → InventoryConfirmed (save) → PaymentPending (save)
    /// </summary>
    private async Task HandleInventoryConfirmedAsync(string body, CancellationToken ct)
    {
        var evt = JsonSerializer.Deserialize<InventoryConfirmedEvent>(body)!;
        using var _  = LogContext.PushProperty("OrderId",       evt.OrderId);
        using var __ = LogContext.PushProperty("CorrelationId", evt.CorrelationId);
        using var ___ = LogContext.PushProperty("ServiceName",  "OrderManagement.API");
        using var ____ = LogContext.PushProperty("EventType",   "InventoryConfirmed");
        logger.LogInformation(
            "OrderEventConsumer: InventoryConfirmed for OrderId={OrderId} CorrelationId={CorrelationId}",
            evt.OrderId, evt.CorrelationId);

        await using var db = CreateDbContext();
        var order = await db.Orders.FindAsync([evt.OrderId], ct);
        if (order is null)
        {
            logger.LogWarning("InventoryConfirmed: order {OrderId} not found", evt.OrderId);
            return;
        }
        if (order.Status != OrderStatus.InventoryPending)
        {
            logger.LogWarning(
                "InventoryConfirmed: order {OrderId} in unexpected status {Status} — skipping",
                evt.OrderId, order.Status);
            return;
        }

        order.Status    = OrderStatus.InventoryConfirmed;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        order.Status    = OrderStatus.PaymentPending;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Order {OrderId} → InventoryConfirmed → PaymentPending", evt.OrderId);
    }

    /// <summary>
    /// inventory.failed:
    ///   InventoryPending → InventoryFailed
    /// </summary>
    private async Task HandleInventoryFailedAsync(string body, CancellationToken ct)
    {
        var evt = JsonSerializer.Deserialize<InventoryFailedEvent>(body)!;
        using var _  = LogContext.PushProperty("OrderId",       evt.OrderId);
        using var __ = LogContext.PushProperty("CorrelationId", evt.CorrelationId);
        using var ___ = LogContext.PushProperty("ServiceName",  "OrderManagement.API");
        using var ____ = LogContext.PushProperty("EventType",   "InventoryFailed");
        logger.LogWarning(
            "OrderEventConsumer: InventoryFailed for OrderId={OrderId} CorrelationId={CorrelationId} Reason={Reason}",
            evt.OrderId, evt.CorrelationId, evt.Reason);

        await using var db = CreateDbContext();
        var order = await db.Orders.FindAsync([evt.OrderId], ct);
        if (order is null || order.Status != OrderStatus.InventoryPending) return;

        order.Status        = OrderStatus.InventoryFailed;
        order.FailureReason = evt.Reason;
        order.UpdatedAt     = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Order {OrderId} → InventoryFailed. Reason: {Reason}", evt.OrderId, evt.Reason);
    }

    /// <summary>
    /// payment.approved:
    ///   PaymentPending → PaymentApproved (save) → ShippingPending (save)
    /// </summary>
    private async Task HandlePaymentApprovedAsync(string body, CancellationToken ct)
    {
        var evt = JsonSerializer.Deserialize<PaymentApprovedEvent>(body)!;
        using var _  = LogContext.PushProperty("OrderId",       evt.OrderId);
        using var __ = LogContext.PushProperty("CorrelationId", evt.CorrelationId);
        using var ___ = LogContext.PushProperty("ServiceName",  "OrderManagement.API");
        using var ____ = LogContext.PushProperty("EventType",   "PaymentApproved");
        logger.LogInformation(
            "OrderEventConsumer: PaymentApproved for OrderId={OrderId} CorrelationId={CorrelationId} TxnId={TransactionId}",
            evt.OrderId, evt.CorrelationId, evt.TransactionId);

        await using var db = CreateDbContext();
        var order = await db.Orders.FindAsync([evt.OrderId], ct);
        if (order is null)
        {
            logger.LogWarning("PaymentApproved: order {OrderId} not found", evt.OrderId);
            return;
        }
        if (order.Status != OrderStatus.PaymentPending)
        {
            logger.LogWarning(
                "PaymentApproved: order {OrderId} in unexpected status {Status} — skipping",
                evt.OrderId, order.Status);
            return;
        }

        order.Status               = OrderStatus.PaymentApproved;
        order.PaymentTransactionId = evt.TransactionId;
        order.UpdatedAt            = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        order.Status    = OrderStatus.ShippingPending;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Order {OrderId} → PaymentApproved → ShippingPending. TxnId: {TxnId}",
            evt.OrderId, evt.TransactionId);
    }

    /// <summary>
    /// payment.failed:
    ///   PaymentPending → PaymentFailed
    /// </summary>
    private async Task HandlePaymentFailedAsync(string body, CancellationToken ct)
    {
        var evt = JsonSerializer.Deserialize<PaymentFailedEvent>(body)!;
        using var _  = LogContext.PushProperty("OrderId",       evt.OrderId);
        using var __ = LogContext.PushProperty("CorrelationId", evt.CorrelationId);
        using var ___ = LogContext.PushProperty("ServiceName",  "OrderManagement.API");
        using var ____ = LogContext.PushProperty("EventType",   "PaymentFailed");
        logger.LogWarning(
            "OrderEventConsumer: PaymentFailed for OrderId={OrderId} CorrelationId={CorrelationId} Reason={Reason}",
            evt.OrderId, evt.CorrelationId, evt.Reason);

        await using var db = CreateDbContext();
        var order = await db.Orders.FindAsync([evt.OrderId], ct);
        if (order is null || order.Status != OrderStatus.PaymentPending) return;

        order.Status        = OrderStatus.PaymentFailed;
        order.FailureReason = evt.Reason;
        order.UpdatedAt     = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Order {OrderId} → PaymentFailed. Reason: {Reason}", evt.OrderId, evt.Reason);
    }

    /// <summary>
    /// shipping.created:
    ///   ShippingPending → ShippingCreated (save) → Completed (save)
    /// </summary>
    private async Task HandleShippingCreatedAsync(string body, CancellationToken ct)
    {
        var evt = JsonSerializer.Deserialize<ShippingCreatedEvent>(body)!;
        using var _  = LogContext.PushProperty("OrderId",       evt.OrderId);
        using var __ = LogContext.PushProperty("CorrelationId", evt.CorrelationId);
        using var ___ = LogContext.PushProperty("ServiceName",  "OrderManagement.API");
        using var ____ = LogContext.PushProperty("EventType",   "ShippingCreated");
        logger.LogInformation(
            "OrderEventConsumer: ShippingCreated for OrderId={OrderId} CorrelationId={CorrelationId} Tracking={TrackingNumber}",
            evt.OrderId, evt.CorrelationId, evt.TrackingNumber);

        await using var db = CreateDbContext();
        var order = await db.Orders.FindAsync([evt.OrderId], ct);
        if (order is null)
        {
            logger.LogWarning("ShippingCreated: order {OrderId} not found", evt.OrderId);
            return;
        }
        if (order.Status != OrderStatus.ShippingPending)
        {
            logger.LogWarning(
                "ShippingCreated: order {OrderId} in unexpected status {Status} — skipping",
                evt.OrderId, order.Status);
            return;
        }

        order.Status         = OrderStatus.ShippingCreated;
        order.TrackingNumber = evt.TrackingNumber;
        order.UpdatedAt      = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        order.Status    = OrderStatus.Completed;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Order {OrderId} → ShippingCreated → Completed. Tracking: {TrackingNumber}",
            evt.OrderId, evt.TrackingNumber);
    }
}
