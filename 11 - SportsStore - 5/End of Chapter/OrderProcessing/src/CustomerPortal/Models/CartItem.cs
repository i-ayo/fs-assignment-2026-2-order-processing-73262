namespace CustomerPortal.Models;

public class CartItem
{
    public long   ProductId   { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int    Quantity    { get; set; }
    public decimal UnitPrice  { get; set; }
    public decimal LineTotal  => Quantity * UnitPrice;
}
