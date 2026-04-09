using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Data;
using Shared.Domain.Enums;

namespace OrderManagement.API.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController(
    OrderDbContext db,
    ILogger<AdminController> logger) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var now   = DateTime.UtcNow;
        var week  = now.AddDays(-7);
        var month = now.AddMonths(-1);

        var completed = await db.Orders
            .Where(o => o.Status == OrderStatus.Completed)
            .ToListAsync();

        var stats = new
        {
            TotalOrders      = await db.Orders.CountAsync(),
            TotalRevenue     = completed.Sum(o => o.TotalAmount),
            RevenueThisWeek  = completed.Where(o => o.CreatedAt >= week).Sum(o => o.TotalAmount),
            RevenueThisMonth = completed.Where(o => o.CreatedAt >= month).Sum(o => o.TotalAmount),
            CompletedOrders  = completed.Count,
            FailedOrders     = await db.Orders.CountAsync(o =>
                o.Status == OrderStatus.Failed ||
                o.Status == OrderStatus.PaymentFailed ||
                o.Status == OrderStatus.InventoryFailed),
        };

        logger.LogInformation(
            "Admin stats requested. TotalOrders: {TotalOrders}, Revenue: {Revenue:C}",
            stats.TotalOrders, stats.TotalRevenue);

        return Ok(stats);
    }
}
