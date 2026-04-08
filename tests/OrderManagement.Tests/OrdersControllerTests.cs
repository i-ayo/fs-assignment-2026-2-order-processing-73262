using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderManagement.API.Controllers;
using OrderManagement.API.Data;
using OrderManagement.API.DTOs;
using OrderManagement.API.Messaging;
using Shared.Domain.Enums;
using Xunit;

namespace OrderManagement.Tests;

/// <summary>
/// Tests for OrdersController covering:
///  - Order creation (POST /api/orders/checkout)
///  - Status transitions via event callbacks
///  - Mapping from entity to DTO (Status serialised correctly)
/// </summary>
public class OrdersControllerTests : IDisposable
{
    private readonly OrderDbContext      _db;
    private readonly Mock<IMessagePublisher> _publisher;
    private readonly OrdersController   _controller;

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

        _controller = new OrdersController(_db, _publisher.Object,
            NullLogger<OrdersController>.Instance);
    }

    // ── Creation ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Checkout_ValidRequest_ReturnsCreated()
    {
        var request = new SubmitOrderRequest(
            "CUST001", "Alice Martin", "1 Main St",
            [new OrderLineRequest(1, "Kayak", 1, 275m)]);

        var result = await _controller.Checkout(request);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var order   = Assert.IsType<OrderResponse>(created.Value);

        Assert.Equal("CUST001",             order.CustomerId);
        Assert.Equal(275m,                  order.TotalAmount);
        // After checkout the order moves straight to InventoryPending
        Assert.Equal(OrderStatus.InventoryPending, order.Status);
    }

    [Fact]
    public async Task Checkout_ValidRequest_PublishesOrderSubmittedEvent()
    {
        var request = new SubmitOrderRequest(
            "CUST002", "Bob Smith", "2 River Rd",
            [new OrderLineRequest(2, "Lifejacket", 2, 48.95m)]);

        await _controller.Checkout(request);

        _publisher.Verify(p => p.PublishAsync(
            "order.submitted",
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Checkout_MultipleLines_SumsTotalCorrectly()
    {
        var request = new SubmitOrderRequest(
            "CUST003", "Carol White", "3 Lake Ave",
            [
                new OrderLineRequest(1, "Kayak",       1, 275m),
                new OrderLineRequest(2, "Lifejacket",  2, 48.95m)
            ]);

        var result = await _controller.Checkout(request);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var order   = Assert.IsType<OrderResponse>(created.Value);

        Assert.Equal(275m + (2 * 48.95m), order.TotalAmount);
    }

    // ── State transitions ────────────────────────────────────────────────────

    [Fact]
    public async Task InventoryConfirmed_TransitionsFromInventoryPendingToPaymentPending()
    {
        var order = await CreateOrderInDb(OrderStatus.InventoryPending);

        var result = await _controller.InventoryConfirmed(order.Id);

        Assert.IsType<NoContentResult>(result);
        var updated = await _db.Orders.FindAsync(order.Id);
        // State machine: InventoryPending → InventoryConfirmed → PaymentPending (two saves)
        Assert.Equal(OrderStatus.PaymentPending, updated!.Status);
    }

    [Fact]
    public async Task InventoryConfirmed_WrongStatus_ReturnsConflict()
    {
        var order = await CreateOrderInDb(OrderStatus.Submitted);

        var result = await _controller.InventoryConfirmed(order.Id);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task InventoryFailed_SetsFailureReason()
    {
        var order = await CreateOrderInDb(OrderStatus.InventoryPending);

        var result = await _controller.InventoryFailed(order.Id, "Out of stock");

        Assert.IsType<NoContentResult>(result);
        var updated = await _db.Orders.FindAsync(order.Id);
        Assert.Equal(OrderStatus.InventoryFailed, updated!.Status);
        Assert.Equal("Out of stock",              updated.FailureReason);
    }

    [Fact]
    public async Task PaymentApproved_SetsTransactionIdAndAdvancesToShippingPending()
    {
        var order = await CreateOrderInDb(OrderStatus.PaymentPending);

        var result = await _controller.PaymentApproved(order.Id, "TXN-123");

        Assert.IsType<NoContentResult>(result);
        var updated = await _db.Orders.FindAsync(order.Id);
        // State machine: PaymentPending → PaymentApproved → ShippingPending (two saves)
        Assert.Equal(OrderStatus.ShippingPending, updated!.Status);
        Assert.Equal("TXN-123",                  updated.PaymentTransactionId);
    }

    [Fact]
    public async Task ShippingCreated_TransitionsToCompleted()
    {
        var order = await CreateOrderInDb(OrderStatus.ShippingPending);

        var result = await _controller.ShippingCreated(order.Id, "TRK-9999");

        Assert.IsType<NoContentResult>(result);
        var updated = await _db.Orders.FindAsync(order.Id);
        // ShippingCreated → Completed in one call
        Assert.Equal(OrderStatus.Completed, updated!.Status);
        Assert.Equal("TRK-9999",           updated.TrackingNumber);
    }

    // ── GetAll filtering ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_FilterByStatus_ReturnsMatchingOrders()
    {
        await CreateOrderInDb(OrderStatus.Completed);
        await CreateOrderInDb(OrderStatus.Failed);
        await CreateOrderInDb(OrderStatus.Completed);

        var result = await _controller.GetAll(status: "Completed", customerId: null);

        var ok     = Assert.IsType<OkObjectResult>(result);
        var orders = Assert.IsAssignableFrom<IEnumerable<OrderResponse>>(ok.Value);
        Assert.All(orders, o => Assert.Equal(OrderStatus.Completed, o.Status));
        Assert.Equal(2, orders.Count());
    }

    [Fact]
    public async Task GetAll_FilterByCustomerId_ReturnsOnlyThatCustomer()
    {
        await CreateOrderInDb(OrderStatus.Submitted, "CUST-A");
        await CreateOrderInDb(OrderStatus.Submitted, "CUST-B");

        var result = await _controller.GetAll(status: null, customerId: "CUST-A");

        var ok     = Assert.IsType<OkObjectResult>(result);
        var orders = Assert.IsAssignableFrom<IEnumerable<OrderResponse>>(ok.Value);
        Assert.All(orders, o => Assert.Equal("CUST-A", o.CustomerId));
    }

    // ── Not found ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_UnknownId_ReturnsNotFound()
    {
        var result = await _controller.GetById(Guid.NewGuid());
        Assert.IsType<NotFoundResult>(result);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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

    public void Dispose() => _db.Dispose();
}
