using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.API.CQRS.Commands;
using OrderManagement.API.CQRS.Queries;
using OrderManagement.API.DTOs;

namespace OrderManagement.API.Controllers;

/// <summary>
/// Thin REST facade — delegates ALL business logic to MediatR commands/queries.
/// Following CQRS:
///   GET  /api/orders          → GetAllOrdersQuery
///   GET  /api/orders/{id}     → GetOrderByIdQuery
///   POST /api/orders/checkout → SubmitOrderCommand
///   PUT  /api/orders/{id}/... → status-transition commands
/// </summary>
[ApiController]
[Route("api/orders")]
public class OrdersController(IMediator mediator, ILogger<OrdersController> logger) : ControllerBase
{
    // ── Queries ───────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? customerId)
    {
        var result = await mediator.Send(new GetAllOrdersQuery(status, customerId));
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await mediator.Send(new GetOrderByIdQuery(id));
        return result is null ? NotFound() : Ok(result);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] SubmitOrderRequest request)
    {
        logger.LogInformation(
            "Checkout request received: CustomerId={CustomerId}, Lines={LineCount}",
            request.CustomerId, request.Lines.Count);

        var order = await mediator.Send(new SubmitOrderCommand(request));
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    // ── Status-transition endpoints (called by OrderEventConsumer / workers) ──

    [HttpPut("{id:guid}/inventory-confirmed")]
    public async Task<IActionResult> InventoryConfirmed(Guid id)
    {
        var result = await mediator.Send(new InventoryConfirmedCommand(id));
        return ToActionResult(result);
    }

    [HttpPut("{id:guid}/inventory-failed")]
    public async Task<IActionResult> InventoryFailed(Guid id, [FromBody] string reason)
    {
        var result = await mediator.Send(new InventoryFailedCommand(id, reason));
        return ToActionResult(result);
    }

    [HttpPut("{id:guid}/payment-approved")]
    public async Task<IActionResult> PaymentApproved(Guid id, [FromBody] string transactionId)
    {
        var result = await mediator.Send(new PaymentApprovedCommand(id, transactionId));
        return ToActionResult(result);
    }

    [HttpPut("{id:guid}/payment-failed")]
    public async Task<IActionResult> PaymentFailed(Guid id, [FromBody] string reason)
    {
        var result = await mediator.Send(new PaymentFailedCommand(id, reason));
        return ToActionResult(result);
    }

    [HttpPut("{id:guid}/shipping-created")]
    public async Task<IActionResult> ShippingCreated(Guid id, [FromBody] string trackingNumber)
    {
        var result = await mediator.Send(new ShippingCreatedCommand(id, trackingNumber));
        return ToActionResult(result);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private IActionResult ToActionResult(StatusCommandResult result) => result.Outcome switch
    {
        StatusCommandOutcome.Success  => NoContent(),
        StatusCommandOutcome.NotFound => NotFound(),
        StatusCommandOutcome.Conflict => Conflict(result.Message),
        _                             => StatusCode(500)
    };
}
