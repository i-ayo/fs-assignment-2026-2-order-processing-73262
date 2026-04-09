using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderManagement.API.CQRS.Queries;
using OrderManagement.API.Data;
using OrderManagement.API.DTOs;

namespace OrderManagement.API.CQRS.Handlers;

/// <summary>
/// Handles <see cref="GetOrderByIdQuery"/>.
/// Returns null when the order is not found; the controller maps that to 404.
/// </summary>
public class GetOrderByIdQueryHandler(
    OrderDbContext db,
    IMapper mapper,
    ILogger<GetOrderByIdQueryHandler> logger)
    : IRequestHandler<GetOrderByIdQuery, OrderResponse?>
{
    public async Task<OrderResponse?> Handle(
        GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("GetOrderByIdQuery: Id={OrderId}", request.Id);

        var order = await db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

        return order is null ? null : mapper.Map<OrderResponse>(order);
    }
}
