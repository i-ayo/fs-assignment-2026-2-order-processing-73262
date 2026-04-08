namespace Shared.Domain.Events;

public record OrderSubmittedEvent(
    Guid OrderId,
    Guid CorrelationId,
    string CustomerId,
    string CustomerName,
    string ShippingAddress,
    decimal TotalAmount,
    List<OrderLineItem> Lines);

public record OrderLineItem(
    Guid LineId,
    long ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);
