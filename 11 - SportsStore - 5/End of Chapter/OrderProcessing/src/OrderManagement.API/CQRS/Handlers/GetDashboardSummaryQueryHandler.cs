using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderManagement.API.CQRS.Queries;
using OrderManagement.API.Data;
using OrderManagement.API.DTOs;
using Shared.Domain.Enums;

namespace OrderManagement.API.CQRS.Handlers;

/// <summary>
/// Handles <see cref="GetDashboardSummaryQuery"/>.
/// Aggregates order counts and revenue figures for the Admin Dashboard.
/// </summary>
public class GetDashboardSummaryQueryHandler(
    OrderDbContext db,
    ILogger<GetDashboardSummaryQueryHandler> logger)
    : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryResponse>
{
    public async Task<DashboardSummaryResponse> Handle(
        GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var now   = DateTime.UtcNow;
        var week  = now.AddDays(-7);
        var month = now.AddMonths(-1);

        var completed = await db.Orders
            .Where(o => o.Status == OrderStatus.Completed)
            .ToListAsync(cancellationToken);

        var summary = new DashboardSummaryResponse
        {
            TotalOrders      = await db.Orders.CountAsync(cancellationToken),
            TotalRevenue     = completed.Sum(o => o.TotalAmount),
            RevenueThisWeek  = completed.Where(o => o.CreatedAt >= week).Sum(o => o.TotalAmount),
            RevenueThisMonth = completed.Where(o => o.CreatedAt >= month).Sum(o => o.TotalAmount),
            CompletedOrders  = completed.Count,
            FailedOrders     = await db.Orders.CountAsync(o =>
                o.Status == OrderStatus.Failed ||
                o.Status == OrderStatus.PaymentFailed ||
                o.Status == OrderStatus.InventoryFailed, cancellationToken)
        };

        logger.LogInformation(
            "GetDashboardSummaryQuery: TotalOrders={TotalOrders}, Revenue={Revenue:C}",
            summary.TotalOrders, summary.TotalRevenue);

        return summary;
    }
}
