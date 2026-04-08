using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Data;
using OrderManagement.API.Data.Entities;
using OrderManagement.API.DTOs;
using OrderManagement.API.Messaging;
using Serilog.Context;
using Shared.Domain.Enums;
using Shared.Domain.Events;

namespace OrderManagement.API.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(
    OrderDbContext db,
    IMessagePublisher publisher,
    ILogger<OrdersController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status,
        [FromQuery] string? customerId)
    {
        var query = db.Orders.Include(o => o.Lines).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(o => o.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(customerId))
        {
            query = query.Where(o => o.CustomerId == customerId);
        }

        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
        return Ok(orders.Select(MapToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var order = await db.Orders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null) return NotFound();
        return Ok(MapToResponse(order));
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] SubmitOrderRequest request)
    {
        logger.LogInformation(
            "Order checkout received. CustomerId: {CustomerId}, CustomerName: {CustomerName}, Lines: {LineCount}",
            request.CustomerId, request.CustomerName, request.Lines.Count);

        var order = new Order
        {
            CustomerId     = request.CustomerId,
            CustomerName   = request.CustomerName,
            ShippingAddress = request.ShippingAddress,
            Status         = OrderStatus.Submitted,
            Lines          = request.Lines.Select(l => new OrderLine
            {
                ProductId   = l.ProductId,
                ProductName = l.ProductName,
                Quantity    = l.Quantity,
                UnitPrice   = l.UnitPrice
            }).ToList()
        };
        order.TotalAmount = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        using var _logOrderId      = LogContext.PushProperty("OrderId",       order.Id);
        using var _logCorrId       = LogContext.PushProperty("CorrelationId", order.CorrelationId);
        using var _logCustomerId   = LogContext.PushProperty("CustomerId",    order.CustomerId);
        using var _logServiceName  = LogContext.PushProperty("ServiceName",   "OrderManagement.API");
        using var _logEventType    = LogContext.PushProperty("EventType",     "OrderSubmitted");
        logger.LogInformation(
            "Order {OrderId} created for CustomerId={CustomerId} Status={Status} Total={Total:C}",
            order.Id, order.CustomerId, order.Status, order.TotalAmount);

        // Publish OrderSubmittedEvent to RabbitMQ → InventoryService picks it up
        var evt = new OrderSubmittedEvent(
            order.Id,
            order.CorrelationId,
            order.CustomerId,
            order.CustomerName,
            order.ShippingAddress,
            order.TotalAmount,
            order.Lines.Select(l => new OrderLineItem(
                l.Id, l.ProductId, l.ProductName, l.Quantity, l.UnitPrice)).ToList());

        await publisher.PublishAsync("order.submitted", evt);

        // Transition to InventoryPending immediately after publishing
        order.Status    = OrderStatus.InventoryPending;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, MapToResponse(order));
    }

    // ── Event callbacks from worker services (called via HTTP) ────────────────

    [HttpPut("{id:guid}/inventory-confirmed")]
    public async Task<IActionResult> InventoryConfirmed(Guid id)
    {
        // State machine step:
        //   InventoryPending → InventoryConfirmed → PaymentPending
        // After confirming inventory we immediately advance to PaymentPending so the
        // PaymentService (listening on inventory.confirmed) knows the next step is payment.
        var order = await db.Orders.FindAsync(id);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.InventoryPending)
            return Conflict($"Unexpected status: {order.Status}");

        order.Status    = OrderStatus.InventoryConfirmed;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        order.Status    = OrderStatus.PaymentPending;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Order {OrderId} inventory confirmed → payment pending", id);
        return NoContent();
    }

    [HttpPut("{id:guid}/inventory-failed")]
    public async Task<IActionResult> InventoryFailed(Guid id, [FromBody] string reason)
    {
        return await TransitionStatus(id, OrderStatus.InventoryPending,
            OrderStatus.InventoryFailed, reason, "inventory-failed");
    }

    [HttpPut("{id:guid}/payment-approved")]
    public async Task<IActionResult> PaymentApproved(Guid id,
        [FromBody] string transactionId)
    {
        // State machine step:
        //   PaymentPending → PaymentApproved → ShippingPending
        // After payment succeeds we advance to ShippingPending so the ShippingService
        // (listening on payment.approved) can create the shipment.
        var order = await db.Orders.FindAsync(id);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.PaymentPending) return Conflict("Unexpected status");

        order.Status               = OrderStatus.PaymentApproved;
        order.PaymentTransactionId = transactionId;
        order.UpdatedAt            = DateTime.UtcNow;
        await db.SaveChangesAsync();

        order.Status    = OrderStatus.ShippingPending;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Order {OrderId} payment approved → shipping pending. TransactionId: {TransactionId}",
            id, transactionId);
        return NoContent();
    }

    [HttpPut("{id:guid}/payment-failed")]
    public async Task<IActionResult> PaymentFailed(Guid id, [FromBody] string reason)
    {
        return await TransitionStatus(id, OrderStatus.PaymentPending,
            OrderStatus.PaymentFailed, reason, "payment-failed");
    }

    [HttpPut("{id:guid}/shipping-created")]
    public async Task<IActionResult> ShippingCreated(Guid id,
        [FromBody] string trackingNumber)
    {
        var order = await db.Orders.FindAsync(id);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.ShippingPending) return Conflict("Unexpected status");

        order.Status         = OrderStatus.ShippingCreated;
        order.TrackingNumber = trackingNumber;
        order.UpdatedAt      = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Mark Completed after shipping created
        order.Status    = OrderStatus.Completed;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Order {OrderId} completed. TrackingNumber: {TrackingNumber}",
            id, trackingNumber);
        return NoContent();
    }

    private async Task<IActionResult> TransitionStatus(
        Guid id, OrderStatus expectedFrom, OrderStatus to,
        string? note, string eventName)
    {
        var order = await db.Orders.FindAsync(id);
        if (order is null) return NotFound();
        if (order.Status != expectedFrom) return Conflict($"Unexpected status: {order.Status}");

        order.Status    = to;
        order.UpdatedAt = DateTime.UtcNow;
        if (note is not null) order.FailureReason = note;
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Order {OrderId} transitioned to {Status} via {Event}",
            id, to, eventName);
        return NoContent();
    }

    private static OrderResponse MapToResponse(Order o) => new()
    {
        Id                   = o.Id,
        CorrelationId        = o.CorrelationId,
        CustomerId           = o.CustomerId,
        CustomerName         = o.CustomerName,
        ShippingAddress      = o.ShippingAddress,
        TotalAmount          = o.TotalAmount,
        Status               = o.Status,
        FailureReason        = o.FailureReason,
        TrackingNumber       = o.TrackingNumber,
        PaymentTransactionId = o.PaymentTransactionId,
        CreatedAt            = o.CreatedAt,
        UpdatedAt            = o.UpdatedAt,
        Lines = o.Lines.Select(l => new OrderLineResponse
        {
            Id          = l.Id,
            ProductId   = l.ProductId,
            ProductName = l.ProductName,
            Quantity    = l.Quantity,
            UnitPrice   = l.UnitPrice,
            LineTotal   = l.Quantity * l.UnitPrice
        }).ToList()
    };
}
