using MediatR;
using OrderManagement.API.DTOs;

namespace OrderManagement.API.CQRS.Queries;

/// <summary>
/// Returns all orders for a given customer, ordered newest-first.
/// Part of the CQRS read-side (Query) — used by GET /api/customers/{id}/orders.
/// </summary>
public record GetCustomerOrdersQuery(string CustomerId) : IRequest<IEnumerable<OrderResponse>>;
