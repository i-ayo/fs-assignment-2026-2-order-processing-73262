using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CustomerPortal.Models;

namespace CustomerPortal.Services;

/// <summary>
/// Thin wrapper around the OrderManagement.API HTTP client.
/// Uses JsonStringEnumConverter so OrderStatus is deserialised from a string
/// ("Completed") rather than an integer – this is the fix for Task A.
/// </summary>
public class OrderApiService(HttpClient http, ILogger<OrderApiService> logger)
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // ── Orders ────────────────────────────────────────────────────────────────

    public async Task<List<OrderResponse>> GetOrdersByCustomerAsync(string customerId)
    {
        logger.LogInformation("Fetching orders for customer {CustomerId}", customerId);
        try
        {
            var result = await http.GetFromJsonAsync<List<OrderResponse>>(
                $"api/customers/{customerId}/orders", _opts);
            return result ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load orders for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<OrderResponse?> SubmitOrderAsync(SubmitOrderRequest request)
    {
        logger.LogInformation(
            "Submitting order for customer {CustomerId}, lines: {Count}",
            request.CustomerId, request.Lines.Count);
        try
        {
            var response = await http.PostAsJsonAsync("api/orders/checkout", request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OrderResponse>(_opts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Order submission failed for {CustomerId}", request.CustomerId);
            throw;
        }
    }

    public async Task<OrderResponse?> GetOrderByIdAsync(Guid orderId)
    {
        try
        {
            return await http.GetFromJsonAsync<OrderResponse>(
                $"api/orders/{orderId}", _opts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load order {OrderId}", orderId);
            return null;
        }
    }

    // ── Inventory ─────────────────────────────────────────────────────────────

    public async Task<int> GetStockAsync(long productId)
    {
        try
        {
            var info = await http.GetFromJsonAsync<StockInfo>(
                $"api/inventory/{productId}", _opts);
            return info?.Quantity ?? 99;
        }
        catch
        {
            return 99; // Default to available on error – fail open
        }
    }

    public async Task<InventoryCheckResult> CheckInventoryAsync(
        IEnumerable<(long ProductId, string Name, int Qty)> items)
    {
        var payload = items.Select(i => new
        {
            productId    = i.ProductId,
            productName  = i.Name,
            requestedQty = i.Qty
        });

        try
        {
            var response = await http.PostAsJsonAsync("api/inventory/check", payload);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<InventoryCheckResult>(_opts)
                ?? new InventoryCheckResult { Available = true };
        }
        catch
        {
            return new InventoryCheckResult { Available = true }; // Fail open
        }
    }
}

public record SubmitOrderRequest(
    string CustomerId,
    string CustomerName,
    string ShippingAddress,
    List<OrderLineRequest> Lines);

public record OrderLineRequest(
    long ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);
