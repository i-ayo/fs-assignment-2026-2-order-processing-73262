using Shared.Domain.Enums;

namespace CustomerPortal.Models;

/// <summary>
/// Client-side DTO – mirrors OrderManagement.API's OrderResponse.
/// Status is kept as the enum type; the HttpClient is configured with
/// JsonStringEnumConverter so it correctly deserialises strings like
/// "Completed" into the enum value.
///
/// FIX for Task A:
///   The original error was:
///     "DeserializeUnableToConvertValue, System.String Path: $[0].status"
///   Root cause: the API serialises Status as a string ("Completed") via
///   JsonStringEnumConverter, but the client's HttpClient had no converter,
///   so it tried to parse the string as an integer enum – and failed.
///   Fix: register JsonStringEnumConverter in the client's JsonSerializerOptions
///   (see CustomerPortal/Program.cs → AddHttpClient configuration).
/// </summary>
public class OrderResponse
{
    public Guid     Id                   { get; set; }
    public string   CustomerId           { get; set; } = string.Empty;
    public string   CustomerName         { get; set; } = string.Empty;
    public string   ShippingAddress      { get; set; } = string.Empty;
    public decimal  TotalAmount          { get; set; }
    public OrderStatus Status            { get; set; }
    public string?  FailureReason        { get; set; }
    public string?  TrackingNumber       { get; set; }
    public string?  PaymentTransactionId { get; set; }
    public DateTime CreatedAt            { get; set; }
    public DateTime UpdatedAt            { get; set; }
    public List<OrderLineResponse> Lines { get; set; } = [];
}

public class OrderLineResponse
{
    public Guid    Id          { get; set; }
    public long    ProductId   { get; set; }
    public string  ProductName { get; set; } = string.Empty;
    public int     Quantity    { get; set; }
    public decimal UnitPrice   { get; set; }
    public decimal LineTotal   { get; set; }
}

public class StockInfo
{
    public long ProductId { get; set; }
    public int  Quantity  { get; set; }
}

public class InventoryCheckResult
{
    public bool          Available { get; set; }
    public List<FailureDetail> Failures { get; set; } = [];
}

public class FailureDetail
{
    public long   ProductId   { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int    Available   { get; set; }
    public int    Requested   { get; set; }
}
