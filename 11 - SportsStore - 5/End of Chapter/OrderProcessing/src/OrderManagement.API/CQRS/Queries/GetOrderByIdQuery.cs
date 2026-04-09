using MediatR;
using OrderManagement.API.DTOs;

namespace OrderManagement.API.CQRS.Queries;

/// <summary>
/// Looks up a single order by its primary key.
/// Returns null when the order does not exist (controller maps to 404).
/// </summary>
public record GetOrderByIdQuery(Guid Id) : IRequest<OrderResponse?>;
