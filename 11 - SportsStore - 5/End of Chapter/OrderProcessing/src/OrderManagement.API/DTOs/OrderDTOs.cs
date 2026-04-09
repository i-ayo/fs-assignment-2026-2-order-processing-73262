using Shared.Domain.Enums;

namespace OrderManagement.API.DTOs;

public record SubmitOrderRequest(
    string CustomerId,
    string CustomerName,
    string ShippingAddress,
    List<OrderLineRequest> Lines);

public record OrderLineRequest(
    long ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);

/// <summary>
/// Serialised with JsonStringEnumConverter so Status arrives as a string
/// (e.g. "Completed") rather than an integer. The CustomerPortal must use
/// the same converter when deserialising – see CustomerPortal/Services/OrderApiService.cs.
/// </summary>
public class OrderResponse
{
    public Guid Id { get; set; }
    public Guid CorrelationId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public string? TrackingNumber { get; set; }
    public string? PaymentTransactionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<OrderLineResponse> Lines { get; set; } = [];
}

public class OrderLineResponse
{
    public Guid Id { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
