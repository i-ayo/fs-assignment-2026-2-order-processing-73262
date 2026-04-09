namespace OrderManagement.API.Messaging;

/// <summary>
/// No-op publisher used in tests and when RabbitMQ is not configured.
/// </summary>
public sealed class InMemoryPublisher(ILogger<InMemoryPublisher> logger) : IMessagePublisher
{
    public Task PublishAsync<T>(string exchange, T message, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[InMemory] Would publish {EventType} to {Exchange}",
            typeof(T).Name, exchange);
        return Task.CompletedTask;
    }
}
