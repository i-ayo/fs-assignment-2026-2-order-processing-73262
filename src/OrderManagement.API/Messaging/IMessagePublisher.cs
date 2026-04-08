namespace OrderManagement.API.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync<T>(string exchange, T message, CancellationToken ct = default);
}
