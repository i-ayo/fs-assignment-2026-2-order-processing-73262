namespace Shared.Domain.Enums;

/// <summary>
/// Represents the lifecycle states an order moves through in the distributed pipeline.
/// Submitted → InventoryPending → InventoryConfirmed/InventoryFailed
///   → PaymentPending → PaymentApproved/PaymentFailed
///   → ShippingPending → ShippingCreated → Completed / Failed
/// </summary>
public enum OrderStatus
{
    Submitted = 0,
    InventoryPending = 1,
    InventoryConfirmed = 2,
    InventoryFailed = 3,
    PaymentPending = 4,
    PaymentApproved = 5,
    PaymentFailed = 6,
    ShippingPending = 7,
    ShippingCreated = 8,
    Completed = 9,
    Failed = 10
}
