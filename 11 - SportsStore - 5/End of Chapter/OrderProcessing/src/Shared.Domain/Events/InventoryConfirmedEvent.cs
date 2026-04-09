namespace Shared.Domain.Events;

public record InventoryConfirmedEvent(
    Guid OrderId,
    Guid CorrelationId);
