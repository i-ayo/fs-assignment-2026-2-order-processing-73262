using MediatR;
using OrderManagement.API.DTOs;

namespace OrderManagement.API.CQRS.Queries;

/// <summary>
/// Returns all orders, optionally filtered by status string and/or customerId.
/// Part of the CQRS read-side (Query).
/// </summary>
public record GetAllOrdersQuery(
    string? Status,
    string? CustomerId) : IRequest<IEnumerable<OrderResponse>>;
