namespace Shared.Domain.Events;

public record InventoryFailedEvent(
    Guid OrderId,
    Guid CorrelationId,
    string Reason);
