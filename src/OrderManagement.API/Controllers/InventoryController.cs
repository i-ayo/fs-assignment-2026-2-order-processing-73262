using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderManagement.API.Data;
using OrderManagement.API.Data.Entities;

namespace OrderManagement.API.Controllers;

/// <summary>
/// Provides simple inventory records for the Customer Portal's stock-aware UI.
/// In a production system this would be a separate InventoryService read model.
/// </summary>
[ApiController]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    private static readonly Dictionary<long, int> _stock = new()
    {
        { 1, 10 }, { 2, 3 }, { 3, 0 }, { 4, 7 }, { 5, 1 },
        { 6, 15 }, { 7, 2 }, { 8, 0 }, { 9, 8 }, { 10, 4 }
    };

    [HttpGet("{productId:long}")]
    public IActionResult GetStock(long productId)
    {
        var qty = _stock.TryGetValue(productId, out var q) ? q : 99;
        return Ok(new { ProductId = productId, Quantity = qty });
    }

    [HttpPost("check")]
    public IActionResult CheckAvailability([FromBody] List<StockCheckRequest> items)
    {
        var failures = new List<object>();
        foreach (var item in items)
        {
            var available = _stock.TryGetValue(item.ProductId, out var q) ? q : 99;
            if (available < item.RequestedQty)
            {
                failures.Add(new
                {
                    item.ProductId,
                    item.ProductName,
                    Available = available,
                    Requested = item.RequestedQty
                });
            }
        }
        return Ok(new { Available = failures.Count == 0, Failures = failures });
    }
}

public record StockCheckRequest(long ProductId, string ProductName, int RequestedQty);
