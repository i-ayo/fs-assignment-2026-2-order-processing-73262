using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Data;
using OrderManagement.API.DTOs;
using Shared.Domain.Enums;

namespace OrderManagement.API.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController(
    OrderDbContext db,
    ILogger<CustomersController> logger) : ControllerBase
{
    /// <summary>
    /// Returns all orders for a given customer.
    /// Used by the My Orders page in the Customer Portal.
    /// Status is serialised as a string via JsonStringEnumConverter (see Program.cs)
    /// so the client must deserialise it the same way.
    /// </summary>
    [HttpGet("{customerId}/orders")]
    public async Task<IActionResult> GetOrdersByCustomer(string customerId)
    {
        logger.LogInformation(
            "Fetching orders for customer {CustomerId}", customerId);

        var orders = await db.Orders
            .Include(o => o.Lines)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var response = orders.Select(o => new OrderResponse
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
        });

        return Ok(response);
    }
}
