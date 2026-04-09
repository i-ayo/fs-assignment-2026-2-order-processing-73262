using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.API.CQRS.Queries;

namespace OrderManagement.API.Controllers;

/// <summary>
/// Thin REST facade — delegates to MediatR query.
/// GET /api/admin/stats → GetDashboardSummaryQuery
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController(
    IMediator mediator,
    ILogger<AdminController> logger) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        logger.LogInformation("Admin dashboard stats requested");
        var result = await mediator.Send(new GetDashboardSummaryQuery());
        return Ok(result);
    }
}
