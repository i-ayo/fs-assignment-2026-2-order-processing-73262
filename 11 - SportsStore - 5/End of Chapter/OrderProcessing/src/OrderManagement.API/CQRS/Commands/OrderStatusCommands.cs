using MediatR;

namespace OrderManagement.API.CQRS.Commands;

/// <summary>
/// State-machine commands — one per allowed status transition.
/// Each command carries the order ID and any extra data required by that step.
/// The handlers return a <see cref="StatusCommandResult"/> that the controller
/// maps to the appropriate HTTP response (204, 404, or 409).
/// </summary>

public record StatusCommandResult(StatusCommandOutcome Outcome, string? Message = null);

public enum StatusCommandOutcome { Success, NotFound, Conflict }

/// <summary>InventoryPending → InventoryConfirmed → PaymentPending</summary>
public record InventoryConfirmedCommand(Guid OrderId) : IRequest<StatusCommandResult>;

/// <summary>InventoryPending → InventoryFailed</summary>
public record InventoryFailedCommand(Guid OrderId, string Reason) : IRequest<StatusCommandResult>;

/// <summary>PaymentPending → PaymentApproved → ShippingPending</summary>
public record PaymentApprovedCommand(Guid OrderId, string TransactionId) : IRequest<StatusCommandResult>;

/// <summary>PaymentPending → PaymentFailed</summary>
public record PaymentFailedCommand(Guid OrderId, string Reason) : IRequest<StatusCommandResult>;

/// <summary>ShippingPending → ShippingCreated → Completed</summary>
public record ShippingCreatedCommand(Guid OrderId, string TrackingNumber) : IRequest<StatusCommandResult>;
