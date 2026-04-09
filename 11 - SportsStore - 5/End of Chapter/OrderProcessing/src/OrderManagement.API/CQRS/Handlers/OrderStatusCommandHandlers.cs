using MediatR;
using OrderManagement.API.CQRS.Commands;
using OrderManagement.API.Data;
using Serilog.Context;
using Shared.Domain.Enums;

namespace OrderManagement.API.CQRS.Handlers;

// ── InventoryConfirmedCommandHandler ──────────────────────────────────────────

/// <summary>
/// InventoryPending → InventoryConfirmed → PaymentPending (two saves, no lost-update).
/// </summary>
public class InventoryConfirmedCommandHandler(
    OrderDbContext db,
    ILogger<InventoryConfirmedCommandHandler> logger)
    : IRequestHandler<InventoryConfirmedCommand, StatusCommandResult>
{
    public async Task<StatusCommandResult> Handle(
        InventoryConfirmedCommand command, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FindAsync([command.OrderId], cancellationToken);
        if (order is null) return new(StatusCommandOutcome.NotFound);
        if (order.Status != OrderStatus.InventoryPending)
            return new(StatusCommandOutcome.Conflict, $"Unexpected status: {order.Status}");

        using var _ = LogContext.PushProperty("OrderId", command.OrderId);

        order.Status    = OrderStatus.InventoryConfirmed;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        order.Status    = OrderStatus.PaymentPending;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Order {OrderId} → InventoryConfirmed → PaymentPending", command.OrderId);

        return new(StatusCommandOutcome.Success);
    }
}

// ── InventoryFailedCommandHandler ─────────────────────────────────────────────

public class InventoryFailedCommandHandler(
    OrderDbContext db,
    ILogger<InventoryFailedCommandHandler> logger)
    : IRequestHandler<InventoryFailedCommand, StatusCommandResult>
{
    public async Task<StatusCommandResult> Handle(
        InventoryFailedCommand command, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FindAsync([command.OrderId], cancellationToken);
        if (order is null) return new(StatusCommandOutcome.NotFound);
        if (order.Status != OrderStatus.InventoryPending)
            return new(StatusCommandOutcome.Conflict, $"Unexpected status: {order.Status}");

        order.Status        = OrderStatus.InventoryFailed;
        order.FailureReason = command.Reason;
        order.UpdatedAt     = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Order {OrderId} → InventoryFailed. Reason: {Reason}",
            command.OrderId, command.Reason);

        return new(StatusCommandOutcome.Success);
    }
}

// ── PaymentApprovedCommandHandler ─────────────────────────────────────────────

/// <summary>
/// PaymentPending → PaymentApproved → ShippingPending (two saves).
/// </summary>
public class PaymentApprovedCommandHandler(
    OrderDbContext db,
    ILogger<PaymentApprovedCommandHandler> logger)
    : IRequestHandler<PaymentApprovedCommand, StatusCommandResult>
{
    public async Task<StatusCommandResult> Handle(
        PaymentApprovedCommand command, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FindAsync([command.OrderId], cancellationToken);
        if (order is null) return new(StatusCommandOutcome.NotFound);
        if (order.Status != OrderStatus.PaymentPending)
            return new(StatusCommandOutcome.Conflict, $"Unexpected status: {order.Status}");

        order.Status               = OrderStatus.PaymentApproved;
        order.PaymentTransactionId = command.TransactionId;
        order.UpdatedAt            = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        order.Status    = OrderStatus.ShippingPending;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Order {OrderId} → PaymentApproved → ShippingPending. TxnId: {TxnId}",
            command.OrderId, command.TransactionId);

        return new(StatusCommandOutcome.Success);
    }
}

// ── PaymentFailedCommandHandler ───────────────────────────────────────────────

public class PaymentFailedCommandHandler(
    OrderDbContext db,
    ILogger<PaymentFailedCommandHandler> logger)
    : IRequestHandler<PaymentFailedCommand, StatusCommandResult>
{
    public async Task<StatusCommandResult> Handle(
        PaymentFailedCommand command, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FindAsync([command.OrderId], cancellationToken);
        if (order is null) return new(StatusCommandOutcome.NotFound);
        if (order.Status != OrderStatus.PaymentPending)
            return new(StatusCommandOutcome.Conflict, $"Unexpected status: {order.Status}");

        order.Status        = OrderStatus.PaymentFailed;
        order.FailureReason = command.Reason;
        order.UpdatedAt     = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Order {OrderId} → PaymentFailed. Reason: {Reason}",
            command.OrderId, command.Reason);

        return new(StatusCommandOutcome.Success);
    }
}

// ── ShippingCreatedCommandHandler ─────────────────────────────────────────────

/// <summary>
/// ShippingPending → ShippingCreated → Completed (two saves).
/// </summary>
public class ShippingCreatedCommandHandler(
    OrderDbContext db,
    ILogger<ShippingCreatedCommandHandler> logger)
    : IRequestHandler<ShippingCreatedCommand, StatusCommandResult>
{
    public async Task<StatusCommandResult> Handle(
        ShippingCreatedCommand command, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FindAsync([command.OrderId], cancellationToken);
        if (order is null) return new(StatusCommandOutcome.NotFound);
        if (order.Status != OrderStatus.ShippingPending)
            return new(StatusCommandOutcome.Conflict, $"Unexpected status: {order.Status}");

        order.Status         = OrderStatus.ShippingCreated;
        order.TrackingNumber = command.TrackingNumber;
        order.UpdatedAt      = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        order.Status    = OrderStatus.Completed;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Order {OrderId} → ShippingCreated → Completed. Tracking: {TrackingNumber}",
            command.OrderId, command.TrackingNumber);

        return new(StatusCommandOutcome.Success);
    }
}
