using BuildingBlocks.Exceptions;

namespace Catalog.Api.Endpoints;

public sealed record ProductDto(
    Guid Id, string Name, string? Description, decimal Price, int AvailableStock, Guid CategoryId, string? CategoryName);

public sealed record CategoryDto(Guid Id, string Name, string? Description);

public sealed record UpsertProductRequest(string Name, string? Description, decimal Price, int AvailableStock, Guid CategoryId)
{
    public void Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 200) errors[nameof(Name)] = ["Name is required (max 200 chars)."];
        if (Price <= 0) errors[nameof(Price)] = ["Price must be greater than 0."];
        if (AvailableStock < 0) errors[nameof(AvailableStock)] = ["AvailableStock cannot be negative."];
        if (CategoryId == Guid.Empty) errors[nameof(CategoryId)] = ["CategoryId is required."];
        if (errors.Count > 0) throw new BadRequestException("Invalid product.", errors);
    }
}

public sealed record CreateCategoryRequest(string Name, string? Description)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100)
            throw new BadRequestException("Invalid category.",
                new Dictionary<string, string[]> { [nameof(Name)] = ["Name is required (max 100 chars)."] });
    }
}
