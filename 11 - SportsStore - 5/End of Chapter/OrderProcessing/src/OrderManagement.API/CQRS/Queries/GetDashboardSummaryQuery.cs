using MediatR;
using OrderManagement.API.DTOs;

namespace OrderManagement.API.CQRS.Queries;

/// <summary>
/// Returns aggregated order statistics for the Admin Dashboard.
/// Part of the CQRS read-side (Query) — used by GET /api/admin/stats.
/// </summary>
public record GetDashboardSummaryQuery : IRequest<DashboardSummaryResponse>;
