namespace Basket.Api.Domain;

public sealed class ShoppingCart
{
    public required string UserName { get; init; }
    public List<CartItem> Items { get; init; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public decimal TotalPrice => Items.Sum(i => i.Price * i.Quantity);

    public void SetItem(Guid productId, string productName, decimal price, int quantity)
    {
        var existing = Items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is null)
            Items.Add(new CartItem { ProductId = productId, ProductName = productName, Price = price, Quantity = quantity });
        else
        {
            existing.ProductName = productName;
            existing.Price = price;
            existing.Quantity = quantity;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public bool RemoveItem(Guid productId)
    {
        var removed = Items.RemoveAll(i => i.ProductId == productId) > 0;
        if (removed) UpdatedAt = DateTime.UtcNow;
        return removed;
    }
}

public sealed class CartItem
{
    public Guid ProductId { get; init; }
    public required string ProductName { get; set; }

    // Snapshot of the Catalog price at the time the item was added/updated.
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}
