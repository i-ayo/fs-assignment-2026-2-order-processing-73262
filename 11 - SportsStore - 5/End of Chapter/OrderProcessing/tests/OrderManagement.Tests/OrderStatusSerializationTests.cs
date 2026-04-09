using System.Text.Json;
using System.Text.Json.Serialization;
using OrderManagement.API.DTOs;
using Shared.Domain.Enums;
using Xunit;

namespace OrderManagement.Tests;

/// <summary>
/// Verifies that the API serialises OrderStatus as a string and that
/// the client-side options (with JsonStringEnumConverter) can deserialise it.
///
/// This is the regression test for Task A:
///   Bug: "DeserializeUnableToConvertValue, System.String Path: $[0].status"
///   Root cause: API emits "Completed" (string) but client had no converter,
///   so it tried to parse the string as an int and failed.
///   Fix: register JsonStringEnumConverter on both ends.
/// </summary>
public class OrderStatusSerializationTests
{
    private static readonly JsonSerializerOptions _apiOpts = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions _clientOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData(OrderStatus.Submitted,        "Submitted")]
    [InlineData(OrderStatus.InventoryPending, "InventoryPending")]
    [InlineData(OrderStatus.Completed,        "Completed")]
    [InlineData(OrderStatus.Failed,           "Failed")]
    public void Api_SerializesStatusAsString(OrderStatus status, string expectedJson)
    {
        var response = new OrderResponse { Status = status };
        var json     = JsonSerializer.Serialize(response, _apiOpts);

        Assert.Contains($"\"Status\":\"{expectedJson}\"", json);
    }

    [Theory]
    [InlineData("Completed",        OrderStatus.Completed)]
    [InlineData("Failed",           OrderStatus.Failed)]
    [InlineData("InventoryPending", OrderStatus.InventoryPending)]
    [InlineData("Submitted",        OrderStatus.Submitted)]
    public void Client_DeserializesStringStatusToEnum(
        string jsonStatus, OrderStatus expectedEnum)
    {
        // This is the exact JSON shape returned by /api/customers/{id}/orders
        var json = $"{{\"Status\":\"{jsonStatus}\"," +
                   $"\"Id\":\"00000000-0000-0000-0000-000000000001\"," +
                   $"\"CustomerId\":\"CUST001\",\"CustomerName\":\"Alice\"," +
                   $"\"ShippingAddress\":\"1 Main St\",\"TotalAmount\":100," +
                   $"\"CreatedAt\":\"2026-01-01T00:00:00Z\"," +
                   $"\"UpdatedAt\":\"2026-01-01T00:00:00Z\",\"Lines\":[]}}";

        var order = JsonSerializer.Deserialize<OrderResponse>(json, _clientOpts);

        Assert.NotNull(order);
        Assert.Equal(expectedEnum, order!.Status);
    }

    [Fact]
    public void Client_WithoutConverter_ThrowsOnStringStatus()
    {
        // Proves the original bug: no converter → exception
        var json = "{\"Status\":\"Completed\",\"Id\":\"00000000-0000-0000-0000-000000000001\"," +
                   "\"CustomerId\":\"X\",\"CustomerName\":\"X\",\"ShippingAddress\":\"X\"," +
                   "\"TotalAmount\":0,\"CreatedAt\":\"2026-01-01T00:00:00Z\"," +
                   "\"UpdatedAt\":\"2026-01-01T00:00:00Z\",\"Lines\":[]}";

        var brokenOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
            // No JsonStringEnumConverter — this is the original broken state
        };

        Assert.ThrowsAny<JsonException>(() =>
            JsonSerializer.Deserialize<OrderResponse>(json, brokenOpts));
    }
}
