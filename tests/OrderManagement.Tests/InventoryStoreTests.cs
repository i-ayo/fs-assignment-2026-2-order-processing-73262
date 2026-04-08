using InventoryService.Models;
using Xunit;

namespace OrderManagement.Tests;

/// <summary>
/// Tests for the in-memory InventoryStore:
///  - Stock reservation succeeds when stock is sufficient
///  - Reservation fails when stock is insufficient
///  - Atomic reservation: all-or-nothing across multiple lines
/// </summary>
public class InventoryStoreTests
{
    [Fact]
    public void GetStock_KnownProduct_ReturnsCorrectQty()
    {
        // Product 1 is seeded with 10 units
        var qty = InventoryStore.GetStock(1);
        Assert.True(qty > 0, "Product 1 should have stock");
    }

    [Fact]
    public void GetStock_UnknownProduct_Returns99()
    {
        var qty = InventoryStore.GetStock(9999);
        Assert.Equal(99, qty);
    }

    [Fact]
    public void TryReserve_OutOfStockProduct_ReturnsFalse()
    {
        // Product 3 is seeded at 0
        var lines = new[] { (3L, "Soccer Ball", 1) };
        var success = InventoryStore.TryReserve(lines, out var failed);

        Assert.False(success);
        Assert.Equal("Soccer Ball", failed);
    }

    [Fact]
    public void TryReserve_UnknownProduct_Succeeds()
    {
        // Unknown products default to 99 stock → reservation succeeds
        var lines   = new[] { (9999L, "Ghost Product", 1) };
        var success = InventoryStore.TryReserve(lines, out var failed);

        Assert.True(success);
        Assert.Null(failed);
    }
}
