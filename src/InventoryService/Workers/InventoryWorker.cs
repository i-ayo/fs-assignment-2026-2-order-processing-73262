using System.Text;
using System.Text.Json;
using InventoryService.Messaging;
using InventoryService.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;
using Shared.Domain.Events;

namespace InventoryService.Workers;

/// <summary>
/// Listens to the "order.submitted" exchange.
/// Checks inventory for each line:
///   - If all items available → publishes InventoryConfirmedEvent to "inventory.confirmed"
///   - If any item unavailable → publishes InventoryFailedEvent to "inventory.failed"
///
/// The OrderManagement.API (or a separate consumer) listens to those exchanges and
/// updates the Order status accordingly, completing the inventory step of the pipeline.
/// </summary>
public class InventoryWorker(IConfiguration config, ILogger<InventoryWorker> logger)
    : BackgroundService
{
    private IConnection? _connection;
    private IChannel?    _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("InventoryWorker starting — connecting to CloudAMQP");

        // Throws InvalidOperationException if credentials are missing — no silent no-op.
        var factory = RabbitMqConnectionFactory.Create(config);

        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel    = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declare source exchange and bind a durable queue to it
        await _channel.ExchangeDeclareAsync("order.submitted", ExchangeType.Fanout,
            durable: true, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync("inventory.order.submitted", durable: true,
            exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync("inventory.order.submitted", "order.submitted",
            routingKey: string.Empty, cancellationToken: stoppingToken);

        // Declare output exchanges
        await _channel.ExchangeDeclareAsync("inventory.confirmed", ExchangeType.Fanout,
            durable: true, cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync("inventory.failed", ExchangeType.Fanout,
            durable: true, cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            logger.LogInformation(
                "InventoryWorker received OrderSubmittedEvent: {Body}", body);

            try
            {
                var evt = JsonSerializer.Deserialize<OrderSubmittedEvent>(body)!;

                using var _logOrderId     = LogContext.PushProperty("OrderId",       evt.OrderId);
                using var _logCorrId      = LogContext.PushProperty("CorrelationId", evt.CorrelationId);
                using var _logCustomerId  = LogContext.PushProperty("CustomerId",    evt.CustomerId);
                using var _logServiceName = LogContext.PushProperty("ServiceName",   "InventoryService");
                using var _logEventType   = LogContext.PushProperty("EventType",     "OrderSubmitted");

                var lines = evt.Lines
                    .Select(l => (l.ProductId, l.ProductName, l.Quantity));

                if (InventoryStore.TryReserve(lines, out var failed))
                {
                    logger.LogInformation(
                        "Inventory CONFIRMED for OrderId={OrderId} CorrelationId={CorrelationId} CustomerId={CustomerId}",
                        evt.OrderId, evt.CorrelationId, evt.CustomerId);

                    await PublishAsync("inventory.confirmed",
                        new InventoryConfirmedEvent(evt.OrderId, evt.CorrelationId),
                        stoppingToken);
                }
                else
                {
                    logger.LogWarning(
                        "Inventory FAILED for OrderId={OrderId} CorrelationId={CorrelationId} Product={Product}",
                        evt.OrderId, evt.CorrelationId, failed);

                    await PublishAsync("inventory.failed",
                        new InventoryFailedEvent(evt.OrderId, evt.CorrelationId,
                            $"{failed} is not available in the requested quantity"),
                        stoppingToken);
                }

                await _channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error processing OrderSubmittedEvent. Nacking.");
                await _channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false,
                    stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync("inventory.order.submitted",
            autoAck: false, consumer: consumer, stoppingToken);

        logger.LogInformation("InventoryWorker listening on inventory.order.submitted");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task PublishAsync<T>(string exchange, T message, CancellationToken ct)
    {
        var json  = JsonSerializer.Serialize(message);
        var body  = Encoding.UTF8.GetBytes(json);
        var props = new BasicProperties { Persistent = true };
        await _channel!.BasicPublishAsync(exchange, routingKey: string.Empty,
            mandatory: false, basicProperties: props, body: body, cancellationToken: ct);
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        logger.LogInformation("InventoryWorker stopping");
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(ct);
    }
}
