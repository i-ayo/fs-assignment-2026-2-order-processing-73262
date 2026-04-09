using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderManagement.API.CQRS.Commands;
using OrderManagement.API.CQRS.Handlers;
using OrderManagement.API.CQRS.Queries;
using OrderManagement.API.Data;
using OrderManagement.API.DTOs;
using OrderManagement.API.Messaging;
using OrderManagement.API.Profiles;
using Shared.Domain.Enums;
using Xunit;

namespace OrderManagement.Tests;

/// <summary>
/// Tests for the order-processing CQRS handlers, covering:
///  - Order creation  (SubmitOrderCommand / SubmitOrderCommandHandler)
///  - Status transitions via the dedicated command handlers
///  - Query handlers  (GetAllOrdersQuery, GetOrderByIdQuery)
///
/// NOTE: The controller was refactored to a thin MediatR facade in the most
/// recent sprint; all business logic now lives in the handlers tested here
/// directly with InMemory EF Core — no full ASP.NET host required.
/// </summary>
public class OrdersControllerTests : IDisposable
{
    private readonly OrderDbContext          _db;
    private readonly Mock<IMessagePublisher> _publisher;
    private readonly IMapper                 _mapper;

    public OrdersControllerTests()
    {
        var opts = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db        = new OrderDbContext(opts);
        _publisher = new Mock<IMessagePublisher>();
        _publisher
            .Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mapper = new MapperConfiguration(cfg =>
            cfg.AddProfile<OrderMappingProfile>()).CreateMapper();
    }

    // ── Private handler factories ─────────────────────────────────────────────
    // Keeps test body code concise; each call creates a fresh handler that
    // shares the same InMemory _db as the test.

    private SubmitOrderCommandHandler SubmitHandler()
        => new(_db, _publisher.Object, _mapper,
               NullLogger<SubmitOrderCommandHandler>.Instance);

    private InventoryConfirmedCommandHandler InventoryConfirmedHandler()
        => new(_db, NullLogger<InventoryConfirmedCommandHandler>.Instance);

    private InventoryFailedCommandHandler InventoryFailedHandler()
        => new(_db, NullLogger<InventoryFailedCommandHandler>.Instance);

    private PaymentApprovedCommandHandler PaymentApprovedHandler()
        => new(_db, NullLogger<PaymentApprovedCommandHandler>.Instance);

    private PaymentFailedCommandHandler PaymentFailedHandler()
        => new(_db, NullLogger<PaymentFailedCommandHandler>.Instance);

    private ShippingCreatedCommandHandler ShippingCreatedHandler()
        => new(_db, NullLogger<ShippingCreatedCommandHandler>.Instance);

    private GetAllOrdersQueryHandler GetAllHandler()
        => new(_db, _mapper, NullLogger<GetAllOrdersQueryHandler>.Instance);

    private GetOrderByIdQueryHandler GetByIdHandler()
        => new(_db, _mapper, NullLogger<GetOrderByIdQueryHandler>.Instance);

    private async Task<OrderManagement.API.Data.Entities.Order> CreateOrderInDb(
        OrderStatus status, string customerId = "CUST-TEST")
    {
        var order = new OrderManagement.API.Data.Entities.Order
        {
            CustomerId      = customerId,
            CustomerName    = "Test Customer",
            ShippingAddress = "1 Test St",
            TotalAmount     = 100m,
            Status          = status
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    // ── SubmitOrderCommand ────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitOrder_ValidRequest_ReturnsOrderAtInventoryPending()
    {
        var request = new SubmitOrderRequest(
            "CUST001", "Alice Martin", "1 Main St",
            [new OrderLineRequest(1, "Kayak", 1, 275m)]);

        var order = await SubmitHandler().Handle(
            new SubmitOrderCommand(request), CancellationToken.None);

        Assert.Equal("CUST001",                   order.CustomerId);
        Assert.Equal(275m,                         order.TotalAmount);
        // Handler immediately advances to InventoryPending after publishing
        Assert.Equal(OrderStatus.InventoryPending, order.Status);
    }

    [Fact]
    public async Task SubmitOrder_ValidRequest_PublishesOrderSubmittedEvent()
    {
        var request = new SubmitOrderRequest(
            "CUST002", "Bob Smith", "2 River Rd",
            [new OrderLineRequest(2, "Lifejacket", 2, 48.95m)]);

        await SubmitHandler().Handle(
            new SubmitOrderCommand(request), CancellationToken.None);

        _publisher.Verify(p => p.PublishAsync(
            "order.submitted",
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitOrder_MultipleLines_SumsTotalCorrectly()
    {
        var request = new SubmitOrderRequest(
            "CUST003", "Carol White", "3 Lake Ave",
            [
                new OrderLineRequest(1, "Kayak",      1, 275m),
                new OrderLineRequest(2, "Lifejacket", 2, 48.95m)
            ]);

        var order = await SubmitHandler().Handle(
            new SubmitOrderCommand(request), CancellationToken.None);

        Assert.Equal(275m + 2 * 48.95m, order.TotalAmount);
    }

    // ── InventoryConfirmedCommand ─────────────────────────────────────────────

    [Fact]
    public async Task InventoryConfirmed_FromInventoryPending_AdvancesToPaymentPending()
    {
        var order = await CreateOrderInDb(OrderStatus.InventoryPending);

        var result = await InventoryConfirmedHandler().Handle(
            new InventoryConfirmedCommand(order.Id), CancellationToken.None);

        Assert.Equal(StatusCommandOutcome.Success, result.Outcome);
        var updated = await _db.Orders.FindAsync(order.Id);
        // Two-save pattern: InventoryPending → InventoryConfirmed → PaymentPending
        Assert.Equal(OrderStatus.PaymentPending, updated!.Status);
    }

    [Fact]
    public async Task InventoryConfirmed_WrongStatus_ReturnsConflict()
    {
        var order = await CreateOrderInDb(OrderStatus.Submitted);

        var result = await InventoryConfirmedHandler().Handle(
            new InventoryConfirmedCommand(order.Id), CancellationToken.None);

        Assert.Equal(StatusCommandOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task InventoryFailed_SetsStatusAndFailureReason()
    {
        var order = await CreateOrderInDb(OrderStatus.InventoryPending);

        var result = await InventoryFailedHandler().Handle(
            new InventoryFailedCommand(order.Id, "Out of stock"), CancellationToken.None);

        Assert.Equal(StatusCommandOutcome.Success, result.Outcome);
        var updated = await _db.Orders.FindAsync(order.Id);
        Assert.Equal(OrderStatus.InventoryFailed, updated!.Status);
        Assert.Equal("Out of stock",              updated.FailureReason);
    }

    // ── PaymentApprovedCommand ────────────────────────────────────────────────

    [Fact]
    public async Task PaymentApproved_SetsTransactionIdAndAdvancesToShippingPending()
    {
        var order = await CreateOrderInDb(OrderStatus.PaymentPending);

        var result = await PaymentApprovedHandler().Handle(
            new PaymentApprovedCommand(order.Id, "TXN-123"), CancellationToken.None);

        Assert.Equal(StatusCommandOutcome.Success, result.Outcome);
        var updated = await _db.Orders.FindAsync(order.Id);
        // Two-save: PaymentPending → PaymentApproved → ShippingPending
        Assert.Equal(OrderStatus.ShippingPending, updated!.Status);
        Assert.Equal("TXN-123",                  updated.PaymentTransactionId);
    }

    [Fact]
    public async Task PaymentFailed_SetsStatusAndReason()
    {
        var order = await CreateOrderInDb(OrderStatus.PaymentPending);

        var result = await PaymentFailedHandler().Handle(
            new PaymentFailedCommand(order.Id, "Card declined"), CancellationToken.None);

        Assert.Equal(StatusCommandOutcome.Success, result.Outcome);
        var updated = await _db.Orders.FindAsync(order.Id);
        Assert.Equal(OrderStatus.PaymentFailed, updated!.Status);
        Assert.Equal("Card declined",          updated.FailureReason);
    }

    // ── ShippingCreatedCommand ────────────────────────────────────────────────

    [Fact]
    public async Task ShippingCreated_TransitionsToCompleted()
    {
        var order = await CreateOrderInDb(OrderStatus.ShippingPending);

        var result = await ShippingCreatedHandler().Handle(
            new ShippingCreatedCommand(order.Id, "TRK-9999"), CancellationToken.None);

        Assert.Equal(StatusCommandOutcome.Success, result.Outcome);
        var updated = await _db.Orders.FindAsync(order.Id);
        // Two-save: ShippingPending → ShippingCreated → Completed
        Assert.Equal(OrderStatus.Completed, updated!.Status);
        Assert.Equal("TRK-9999",           updated.TrackingNumber);
    }

    // ── GetAllOrdersQuery ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_FilterByStatus_ReturnsMatchingOrders()
    {
        await CreateOrderInDb(OrderStatus.Completed);
        await CreateOrderInDb(OrderStatus.Failed);
        await CreateOrderInDb(OrderStatus.Completed);

        var orders = await GetAllHandler().Handle(
            new GetAllOrdersQuery("Completed", null), CancellationToken.None);

        Assert.All(orders, o => Assert.Equal(OrderStatus.Completed, o.Status));
        Assert.Equal(2, orders.Count());
    }

    [Fact]
    public async Task GetAll_FilterByCustomerId_ReturnsOnlyThatCustomer()
    {
        await CreateOrderInDb(OrderStatus.Submitted, "CUST-A");
        await CreateOrderInDb(OrderStatus.Submitted, "CUST-B");

        var orders = await GetAllHandler().Handle(
            new GetAllOrdersQuery(null, "CUST-A"), CancellationToken.None);

        Assert.All(orders, o => Assert.Equal("CUST-A", o.CustomerId));
    }

    // ── GetOrderByIdQuery ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_UnknownId_ReturnsNull()
    {
        var result = await GetByIdHandler().Handle(
            new GetOrderByIdQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetById_KnownId_ReturnsCorrectOrder()
    {
        var order = await CreateOrderInDb(OrderStatus.Completed, "CUST001");

        var result = await GetByIdHandler().Handle(
            new GetOrderByIdQuery(order.Id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(order.Id,              result!.Id);
        Assert.Equal("CUST001",             result.CustomerId);
        Assert.Equal(OrderStatus.Completed, result.Status);
    }

    public void Dispose() => _db.Dispose();
}
