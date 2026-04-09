namespace Shared.Domain.Events;

public record ShippingCreatedEvent(
    Guid OrderId,
    Guid CorrelationId,
    string TrackingNumber);
