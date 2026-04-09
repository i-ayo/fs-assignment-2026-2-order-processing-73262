using System.Text;
using System.Text.Json;
using ShippingService.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;
using Shared.Domain.Events;

namespace ShippingService.Workers;

/// <summary>
/// Listens to "payment.approved" exchange.
/// Generates a tracking number and publishes ShippingCreatedEvent.
/// This is the final step in the pipeline; the OrderManagement.API then
/// marks the order Completed.
/// </summary>
public class ShippingWorker(IConfiguration config, ILogger<ShippingWorker> logger)
    : BackgroundService
{
    private IConnection? _connection;
    private IChannel?    _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ShippingWorker starting — connecting to CloudAMQP");

        // Throws InvalidOperationException if credentials are missing — no silent no-op.
        var factory = RabbitMqConnectionFactory.Create(config);

        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel    = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync("payment.approved", ExchangeType.Fanout,
            durable: true, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync("shipping.payment.approved", durable: true,
            exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync("shipping.payment.approved", "payment.approved",
            routingKey: string.Empty, cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync("shipping.created", ExchangeType.Fanout,
            durable: true, cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            logger.LogInformation("ShippingWorker received PaymentApprovedEvent: {Body}", body);

            try
            {
                var evt = JsonSerializer.Deserialize<PaymentApprovedEvent>(body)!;

                using var _logOrderId     = LogContext.PushProperty("OrderId",       evt.OrderId);
                using var _logCorrId      = LogContext.PushProperty("CorrelationId", evt.CorrelationId);
                using var _logServiceName = LogContext.PushProperty("ServiceName",   "ShippingService");
                using var _logEventType   = LogContext.PushProperty("EventType",     "PaymentApproved");

                // Simulate a small delay for label generation
                await Task.Delay(200, stoppingToken);

                var tracking = $"TRK-{DateTime.UtcNow:yyyyMMdd}-{evt.OrderId:N}"[..20];

                logger.LogInformation(
                    "Shipping label created for OrderId={OrderId} CorrelationId={CorrelationId} Tracking={Tracking}",
                    evt.OrderId, evt.CorrelationId, tracking);

                await PublishAsync("shipping.created",
                    new ShippingCreatedEvent(evt.OrderId, evt.CorrelationId, tracking),
                    stoppingToken);

                await _channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing PaymentApprovedEvent. Nacking.");
                await _channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false,
                    stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync("shipping.payment.approved",
            autoAck: false, consumer: consumer, stoppingToken);

        logger.LogInformation("ShippingWorker listening on shipping.payment.approved");
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
        logger.LogInformation("ShippingWorker stopping");
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(ct);
    }
}
