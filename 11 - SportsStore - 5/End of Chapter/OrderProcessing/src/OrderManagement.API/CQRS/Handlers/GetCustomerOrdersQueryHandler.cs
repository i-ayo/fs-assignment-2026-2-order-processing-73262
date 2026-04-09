using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderManagement.API.CQRS.Queries;
using OrderManagement.API.Data;
using OrderManagement.API.DTOs;

namespace OrderManagement.API.CQRS.Handlers;

/// <summary>
/// Handles <see cref="GetCustomerOrdersQuery"/>.
/// Returns all orders for a specific customer, newest-first, mapped to DTOs.
/// </summary>
public class GetCustomerOrdersQueryHandler(
    OrderDbContext db,
    IMapper mapper,
    ILogger<GetCustomerOrdersQueryHandler> logger)
    : IRequestHandler<GetCustomerOrdersQuery, IEnumerable<OrderResponse>>
{
    public async Task<IEnumerable<OrderResponse>> Handle(
        GetCustomerOrdersQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "GetCustomerOrdersQuery: CustomerId={CustomerId}", request.CustomerId);

        var orders = await db.Orders
            .Include(o => o.Lines)
            .Where(o => o.CustomerId == request.CustomerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        return mapper.Map<IEnumerable<OrderResponse>>(orders);
    }
}
