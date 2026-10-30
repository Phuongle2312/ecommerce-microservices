using BuildingBlocks.Exceptions;
using BuildingBlocks.Pagination;
using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products").WithTags("Products");

        group.MapGet("/", async ([AsParameters] PaginationRequest page, Guid? categoryId, string? search, CatalogDbContext db, CancellationToken ct) =>
        {
            var query = db.Products.AsNoTracking();
            if (categoryId is not null) query = query.Where(p => p.CategoryId == categoryId);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => EF.Functions.ILike(p.Name, $"%{search}%"));

            var total = await query.LongCountAsync(ct);
            var items = await query
                .OrderBy(p => p.Name)
                .Skip(page.Skip).Take(page.SafePageSize)
                .Select(ToDto)
                .ToListAsync(ct);

            return TypedResults.Ok(new PaginatedResult<ProductDto>(page.SafePageIndex, page.SafePageSize, total, items));
        });

        group.MapGet("/{id:guid}", async (Guid id, CatalogDbContext db, CancellationToken ct) =>
        {
            var product = await db.Products.AsNoTracking().Where(p => p.Id == id).Select(ToDto).FirstOrDefaultAsync(ct)
                          ?? throw new NotFoundException("Product", id);
            return TypedResults.Ok(product);
        }).WithName("GetProductById");

        group.MapPost("/", async (UpsertProductRequest request, CatalogDbContext db, CancellationToken ct) =>
        {
            request.Validate();
            await EnsureCategoryExists(db, request.CategoryId, ct);

            var product = new Product
            {
                Name = request.Name.Trim(),
                Description = request.Description,
                Price = request.Price,
                AvailableStock = request.AvailableStock,
                CategoryId = request.CategoryId
            };
            db.Products.Add(product);
            await db.SaveChangesAsync(ct);

            return TypedResults.CreatedAtRoute(new { product.Id }, "GetProductById", new { id = product.Id });
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertProductRequest request, CatalogDbContext db, CancellationToken ct) =>
        {
            request.Validate();
            var product = await db.Products.FindAsync([id], ct) ?? throw new NotFoundException("Product", id);
            if (product.CategoryId != request.CategoryId) await EnsureCategoryExists(db, request.CategoryId, ct);

            product.Name = request.Name.Trim();
            product.Description = request.Description;
            product.Price = request.Price;
            product.AvailableStock = request.AvailableStock;
            product.CategoryId = request.CategoryId;
            product.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return TypedResults.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, CatalogDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
            if (deleted == 0) throw new NotFoundException("Product", id);
            return TypedResults.NoContent();
        });

        return app;
    }

    private static readonly System.Linq.Expressions.Expression<Func<Product, ProductDto>> ToDto = p =>
        new ProductDto(p.Id, p.Name, p.Description, p.Price, p.AvailableStock, p.CategoryId, p.Category!.Name);

    private static async Task EnsureCategoryExists(CatalogDbContext db, Guid categoryId, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
            throw new BadRequestException("Invalid product.",
                new Dictionary<string, string[]> { ["CategoryId"] = [$"Category '{categoryId}' does not exist."] });
    }
}
