namespace Shared.Domain.Events;

public record PaymentFailedEvent(
    Guid OrderId,
    Guid CorrelationId,
    string Reason);
