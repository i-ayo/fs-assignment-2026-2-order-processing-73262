using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderManagement.API.CQRS.Queries;
using OrderManagement.API.Data;
using OrderManagement.API.DTOs;
using Shared.Domain.Enums;

namespace OrderManagement.API.CQRS.Handlers;

/// <summary>
/// Handles <see cref="GetAllOrdersQuery"/>.
/// Queries the database, applies optional filters, and maps entities to DTOs
/// via AutoMapper.
/// </summary>
public class GetAllOrdersQueryHandler(
    OrderDbContext db,
    IMapper mapper,
    ILogger<GetAllOrdersQueryHandler> logger)
    : IRequestHandler<GetAllOrdersQuery, IEnumerable<OrderResponse>>
{
    public async Task<IEnumerable<OrderResponse>> Handle(
        GetAllOrdersQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "GetAllOrdersQuery: Status={Status} CustomerId={CustomerId}",
            request.Status, request.CustomerId);

        var query = db.Orders.Include(o => o.Lines).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<OrderStatus>(request.Status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(o => o.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(request.CustomerId))
        {
            query = query.Where(o => o.CustomerId == request.CustomerId);
        }

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        return mapper.Map<IEnumerable<OrderResponse>>(orders);
    }
}
