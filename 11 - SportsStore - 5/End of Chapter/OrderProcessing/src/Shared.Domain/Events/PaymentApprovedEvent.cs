namespace Shared.Domain.Events;

public record PaymentApprovedEvent(
    Guid OrderId,
    Guid CorrelationId,
    string TransactionId);
