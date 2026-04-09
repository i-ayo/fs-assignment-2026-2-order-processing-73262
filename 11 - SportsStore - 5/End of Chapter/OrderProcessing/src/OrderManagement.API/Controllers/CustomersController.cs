using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.API.CQRS.Queries;

namespace OrderManagement.API.Controllers;

/// <summary>
/// Thin REST facade — delegates to MediatR query.
/// GET /api/customers/{id}/orders → GetCustomerOrdersQuery
/// </summary>
[ApiController]
[Route("api/customers")]
public class CustomersController(
    IMediator mediator,
    ILogger<CustomersController> logger) : ControllerBase
{
    [HttpGet("{customerId}/orders")]
    public async Task<IActionResult> GetOrdersByCustomer(string customerId)
    {
        logger.LogInformation(
            "Customer orders request: CustomerId={CustomerId}", customerId);

        var result = await mediator.Send(new GetCustomerOrdersQuery(customerId));
        return Ok(result);
    }
}
