namespace Catalog.Api.Domain;

public sealed class Product
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }

    // Catalog owns stock until the Inventory service takes over in phase 2.
    public int AvailableStock { get; set; }

    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
