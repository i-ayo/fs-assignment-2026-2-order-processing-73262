using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace OrderManagement.API.Messaging;

/// <summary>
/// Publishes domain events to RabbitMQ topic exchanges.
/// Each event type gets its own exchange (fanout) so every consumer queue
/// bound to that exchange receives a copy.
/// </summary>
public sealed class RabbitMqPublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly ILogger<RabbitMqPublisher> _logger;

    private RabbitMqPublisher(IConnection connection, IChannel channel,
        ILogger<RabbitMqPublisher> logger)
    {
        _connection = connection;
        _channel    = channel;
        _logger     = logger;
    }

    public static async Task<RabbitMqPublisher> CreateAsync(
        IConfiguration config, ILogger<RabbitMqPublisher> logger)
    {
        // Throws InvalidOperationException loudly if Host/Username/Password/VirtualHost missing.
        var factory = RabbitMqConnectionFactory.Create(config);
        var conn    = await factory.CreateConnectionAsync();
        var channel = await conn.CreateChannelAsync();
        return new RabbitMqPublisher(conn, channel, logger);
    }

    public async Task PublishAsync<T>(string exchange, T message, CancellationToken ct = default)
    {
        await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true,
            cancellationToken: ct);

        var json  = JsonSerializer.Serialize(message);
        var body  = Encoding.UTF8.GetBytes(json);

        var props = new BasicProperties { Persistent = true };
        await _channel.BasicPublishAsync(exchange, routingKey: string.Empty,
            mandatory: false, basicProperties: props, body: body, cancellationToken: ct);

        _logger.LogInformation(
            "Published {EventType} to exchange {Exchange}: {Payload}",
            typeof(T).Name, exchange, json);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
