using AutoMapper;
using MediatR;
using OrderManagement.API.CQRS.Commands;
using OrderManagement.API.Data;
using OrderManagement.API.Data.Entities;
using OrderManagement.API.DTOs;
using OrderManagement.API.Messaging;
using Serilog.Context;
using Shared.Domain.Enums;
using Shared.Domain.Events;

namespace OrderManagement.API.CQRS.Handlers;

/// <summary>
/// Handles <see cref="SubmitOrderCommand"/>.
/// Persists the order, publishes OrderSubmittedEvent to RabbitMQ, then
/// advances the status to InventoryPending.
/// </summary>
public class SubmitOrderCommandHandler(
    OrderDbContext db,
    IMessagePublisher publisher,
    IMapper mapper,
    ILogger<SubmitOrderCommandHandler> logger)
    : IRequestHandler<SubmitOrderCommand, OrderResponse>
{
    public async Task<OrderResponse> Handle(
        SubmitOrderCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        logger.LogInformation(
            "SubmitOrderCommand: CustomerId={CustomerId}, CustomerName={CustomerName}, Lines={LineCount}",
            request.CustomerId, request.CustomerName, request.Lines.Count);

        var order = new Order
        {
            CustomerId      = request.CustomerId,
            CustomerName    = request.CustomerName,
            ShippingAddress = request.ShippingAddress,
            Status          = OrderStatus.Submitted,
            Lines           = request.Lines.Select(l => new OrderLine
            {
                ProductId   = l.ProductId,
                ProductName = l.ProductName,
                Quantity    = l.Quantity,
                UnitPrice   = l.UnitPrice
            }).ToList()
        };
        order.TotalAmount = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        using var _logOrderId     = LogContext.PushProperty("OrderId",       order.Id);
        using var _logCorrId      = LogContext.PushProperty("CorrelationId", order.CorrelationId);
        using var _logCustomerId  = LogContext.PushProperty("CustomerId",    order.CustomerId);
        using var _logServiceName = LogContext.PushProperty("ServiceName",   "OrderManagement.API");
        using var _logEventType   = LogContext.PushProperty("EventType",     "OrderSubmitted");

        logger.LogInformation(
            "Order {OrderId} created for CustomerId={CustomerId} Status={Status} Total={Total:C}",
            order.Id, order.CustomerId, order.Status, order.TotalAmount);

        // Publish to RabbitMQ → InventoryService
        var evt = new OrderSubmittedEvent(
            order.Id,
            order.CorrelationId,
            order.CustomerId,
            order.CustomerName,
            order.ShippingAddress,
            order.TotalAmount,
            order.Lines.Select(l => new OrderLineItem(
                l.Id, l.ProductId, l.ProductName, l.Quantity, l.UnitPrice)).ToList());

        await publisher.PublishAsync("order.submitted", evt, cancellationToken);

        // Advance to InventoryPending immediately after publish
        order.Status    = OrderStatus.InventoryPending;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Order {OrderId} advanced to InventoryPending", order.Id);

        return mapper.Map<OrderResponse>(order);
    }
}
