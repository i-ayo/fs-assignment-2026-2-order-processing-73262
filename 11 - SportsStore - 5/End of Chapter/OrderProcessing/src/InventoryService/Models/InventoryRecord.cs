namespace InventoryService.Models;

/// <summary>
/// In-memory inventory store. In production this would be backed by a database.
/// Products 3 and 8 are seeded at 0 to demonstrate out-of-stock behaviour.
/// Products 2, 5, 7 are seeded low (1-3) to trigger "Only X left!" in the UI.
/// </summary>
public static class InventoryStore
{
    // ProductId → current stock quantity
    private static readonly Dictionary<long, int> _stock = new()
    {
        { 1, 10 }, { 2, 3 }, { 3, 0 }, { 4, 7 }, { 5, 1 },
        { 6, 15 }, { 7, 2 }, { 8, 0 }, { 9, 8 }, { 10, 4 }
    };

    private static readonly object _lock = new();

    public static int GetStock(long productId)
    {
        lock (_lock)
        {
            return _stock.TryGetValue(productId, out var q) ? q : 99;
        }
    }

    /// <summary>
    /// Attempts to reserve (decrement) stock for all lines atomically.
    /// Returns false and the failing product name if any line cannot be fulfilled.
    /// </summary>
    public static bool TryReserve(IEnumerable<(long ProductId, string Name, int Qty)> lines,
        out string? failedProduct)
    {
        lock (_lock)
        {
            // Check first (do not decrement until all pass)
            foreach (var (id, name, qty) in lines)
            {
                var available = _stock.TryGetValue(id, out var q) ? q : 99;
                if (available < qty)
                {
                    failedProduct = name;
                    return false;
                }
            }
            // All good — decrement
            foreach (var (id, _, qty) in lines)
            {
                if (_stock.ContainsKey(id))
                    _stock[id] -= qty;
            }
            failedProduct = null;
            return true;
        }
    }
}
