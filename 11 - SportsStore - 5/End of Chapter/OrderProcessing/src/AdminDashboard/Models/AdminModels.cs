using Shared.Domain.Enums;

namespace AdminDashboard.Models;

public class OrderSummary
{
    public Guid      Id                   { get; set; }
    public string    CustomerId           { get; set; } = string.Empty;
    public string    CustomerName         { get; set; } = string.Empty;
    public string    ShippingAddress      { get; set; } = string.Empty;
    public decimal   TotalAmount          { get; set; }
    public OrderStatus Status             { get; set; }
    public string?   FailureReason        { get; set; }
    public string?   TrackingNumber       { get; set; }
    public string?   PaymentTransactionId { get; set; }
    public DateTime  CreatedAt            { get; set; }
    public DateTime  UpdatedAt            { get; set; }
    public List<OrderLineSummary> Lines   { get; set; } = [];
}

public class OrderLineSummary
{
    public long    ProductId   { get; set; }
    public string  ProductName { get; set; } = string.Empty;
    public int     Quantity    { get; set; }
    public decimal UnitPrice   { get; set; }
    public decimal LineTotal   { get; set; }
}

public class AdminStats
{
    public int     TotalOrders      { get; set; }
    public decimal TotalRevenue     { get; set; }
    public decimal RevenueThisWeek  { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public int     CompletedOrders  { get; set; }
    public int     FailedOrders     { get; set; }
}
