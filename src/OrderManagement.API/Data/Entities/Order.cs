using Shared.Domain.Enums;

namespace OrderManagement.API.Data.Entities;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Submitted;
    public string? FailureReason { get; set; }
    public string? TrackingNumber { get; set; }
    public string? PaymentTransactionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<OrderLine> Lines { get; set; } = [];
}
