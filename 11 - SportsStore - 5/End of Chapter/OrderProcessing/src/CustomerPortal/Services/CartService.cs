using CustomerPortal.Models;

namespace CustomerPortal.Services;

/// <summary>
/// Per-circuit (Scoped) in-memory cart.
///
/// Registered as AddScoped so each Blazor Server circuit (browser session)
/// gets its own independent cart. Previously AddSingleton caused all open
/// tabs/users to share a single cart instance — which also made the
/// StateChanged event fire across every connected circuit.
///
/// A lock guards the List mutations so that StateChanged subscribers
/// (e.g. the nav-bar badge) cannot read _items while it is being modified.
/// </summary>
public class CartService
{
    private readonly List<CartItem> _items = [];
    private readonly object _lock = new();

    public IReadOnlyList<CartItem> Items
    {
        get { lock (_lock) { return _items.ToList().AsReadOnly(); } }
    }

    public int TotalQuantity
    {
        get { lock (_lock) { return _items.Sum(i => i.Quantity); } }
    }

    public decimal TotalPrice
    {
        get { lock (_lock) { return _items.Sum(i => i.LineTotal); } }
    }

    /// <summary>
    /// Fired after every mutation so subscribed components (e.g. nav badge)
    /// can call StateHasChanged. Subscribers must use InvokeAsync(StateHasChanged)
    /// to marshal back onto their own Blazor synchronisation context.
    /// </summary>
    public event Action? StateChanged;

    public void AddItem(long productId, string name, decimal price, int qty = 1)
    {
        lock (_lock)
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
        }
        StateChanged?.Invoke();
    }

    public void SetQuantity(long productId, int qty)
    {
        bool changed;
        lock (_lock)
        {
            var item = _items.FirstOrDefault(i => i.ProductId == productId);
            if (item is null) { changed = false; }
            else if (qty <= 0) { _items.Remove(item); changed = true; }
            else { item.Quantity = qty; changed = true; }
        }
        if (changed) StateChanged?.Invoke();
    }

    public void RemoveItem(long productId)
    {
        bool changed;
        lock (_lock)
        {
            var item = _items.FirstOrDefault(i => i.ProductId == productId);
            changed = item is not null && _items.Remove(item);
        }
        if (changed) StateChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_lock) { _items.Clear(); }
        StateChanged?.Invoke();
    }
}
