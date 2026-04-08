using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdminDashboard.Models;

namespace AdminDashboard.Services;

public class AdminApiService(HttpClient http, ILogger<AdminApiService> logger)
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<List<OrderSummary>> GetOrdersAsync(
        string? statusFilter = null, string? customerIdFilter = null)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(statusFilter))   qs.Add($"status={statusFilter}");
        if (!string.IsNullOrWhiteSpace(customerIdFilter)) qs.Add($"customerId={customerIdFilter}");
        var url = "api/orders" + (qs.Any() ? "?" + string.Join("&", qs) : "");

        try
        {
            return await http.GetFromJsonAsync<List<OrderSummary>>(url, _opts) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch orders");
            return [];
        }
    }

    public async Task<AdminStats> GetStatsAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<AdminStats>("api/admin/stats", _opts)
                ?? new AdminStats();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch admin stats");
            return new AdminStats();
        }
    }
}
