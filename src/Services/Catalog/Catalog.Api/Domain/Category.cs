namespace Catalog.Api.Domain;

public sealed class Category
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public string? Description { get; set; }

    public List<Product> Products { get; } = [];
}
