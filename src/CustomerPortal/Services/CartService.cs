using CustomerPortal.Models;

namespace CustomerPortal.Services;

/// <summary>
/// Singleton in-memory cart. Fires StateChanged so any subscribed Blazor
/// component (e.g. the NavBar badge) re-renders reactively.
/// </summary>
public class CartService
{
    private readonly List<CartItem> _items = [];

    public IReadOnlyList<CartItem> Items => _items.AsReadOnly();

    public int TotalQuantity  => _items.Sum(i => i.Quantity);
    public decimal TotalPrice => _items.Sum(i => i.LineTotal);

    public event Action? StateChanged;

    public void AddItem(long productId, string name, decimal price, int qty = 1)
    {
        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null)
            existing.Quantity += qty;
        else
            _items.Add(new CartItem
            {
                ProductId   = productId,
                ProductName = name,
                UnitPrice   = price,
                Quantity    = qty
            });
        StateChanged?.Invoke();
    }

    public void SetQuantity(long productId, int qty)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        if (item is null) return;
        if (qty <= 0)
            _items.Remove(item);
        else
            item.Quantity = qty;
        StateChanged?.Invoke();
    }

    public void RemoveItem(long productId)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        if (item is not null)
        {
            _items.Remove(item);
            StateChanged?.Invoke();
        }
    }

    public void Clear()
    {
        _items.Clear();
        StateChanged?.Invoke();
    }
}
