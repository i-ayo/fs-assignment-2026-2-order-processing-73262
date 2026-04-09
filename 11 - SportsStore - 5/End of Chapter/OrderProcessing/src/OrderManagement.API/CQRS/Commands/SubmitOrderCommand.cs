using MediatR;
using OrderManagement.API.DTOs;

namespace OrderManagement.API.CQRS.Commands;

/// <summary>
/// Write-side command: validates and persists a new order, then publishes
/// OrderSubmittedEvent to RabbitMQ so the InventoryService can act on it.
/// Returns the newly-created OrderResponse (used by the controller to build
/// a 201 Created response).
/// </summary>
public record SubmitOrderCommand(SubmitOrderRequest Request) : IRequest<OrderResponse>;
