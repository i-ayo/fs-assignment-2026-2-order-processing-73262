using System.Text;
using System.Text.Json;
using PaymentService.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;
using Shared.Domain.Events;

namespace PaymentService.Workers;

/// <summary>
/// Listens to "inventory.confirmed" exchange.
/// Simulates payment processing:
///   - 90% success  → publishes PaymentApprovedEvent to "payment.approved"
///   - 10% failure  → publishes PaymentFailedEvent to "payment.failed"
///
/// In production this would call Stripe / a payment gateway.
/// </summary>
public class PaymentWorker(IConfiguration config, ILogger<PaymentWorker> logger)
    : BackgroundService
{
    private static readonly Random _rng = new();
    private IConnection? _connection;
    private IChannel?    _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PaymentWorker starting — connecting to CloudAMQP");

        // Throws InvalidOperationException if credentials are missing — no silent no-op.
        var factory = RabbitMqConnectionFactory.Create(config);

        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel    = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync("inventory.confirmed", ExchangeType.Fanout,
            durable: true, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync("payment.inventory.confirmed", durable: true,
            exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync("payment.inventory.confirmed", "inventory.confirmed",
            routingKey: string.Empty, cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync("payment.approved", ExchangeType.Fanout,
            durable: true, cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync("payment.failed", ExchangeType.Fanout,
            durable: true, cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            logger.LogInformation("PaymentWorker received InventoryConfirmedEvent: {Body}", body);

            try
            {
                var evt = JsonSerializer.Deserialize<InventoryConfirmedEvent>(body)!;

                using var _logOrderId     = LogContext.PushProperty("OrderId",       evt.OrderId);
                using var _logCorrId      = LogContext.PushProperty("CorrelationId", evt.CorrelationId);
                using var _logServiceName = LogContext.PushProperty("ServiceName",   "PaymentService");
                using var _logEventType   = LogContext.PushProperty("EventType",     "InventoryConfirmed");

                // Simulate 90% payment success
                if (_rng.NextDouble() < 0.9)
                {
                    var txnId = $"TXN-{Guid.NewGuid():N}"[..16];
                    logger.LogInformation(
                        "Payment APPROVED for OrderId={OrderId} CorrelationId={CorrelationId} TxnId={TxnId}",
                        evt.OrderId, evt.CorrelationId, txnId);

                    await PublishAsync("payment.approved",
                        new PaymentApprovedEvent(evt.OrderId, evt.CorrelationId, txnId),
                        stoppingToken);
                }
                else
                {
                    logger.LogWarning(
                        "Payment FAILED (simulated decline) for OrderId={OrderId} CorrelationId={CorrelationId}",
                        evt.OrderId, evt.CorrelationId);

                    await PublishAsync("payment.failed",
                        new PaymentFailedEvent(evt.OrderId, evt.CorrelationId,
                            "Card declined (simulated)"),
                        stoppingToken);
                }

                await _channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing InventoryConfirmedEvent. Nacking.");
                await _channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false,
                    stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync("payment.inventory.confirmed",
            autoAck: false, consumer: consumer, stoppingToken);

        logger.LogInformation("PaymentWorker listening on payment.inventory.confirmed");
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
        logger.LogInformation("PaymentWorker stopping");
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(ct);
    }
}
