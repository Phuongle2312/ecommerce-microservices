using BuildingBlocks.Exceptions;
using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categories").WithTags("Categories");

        group.MapGet("/", async (CatalogDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await db.Categories.AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryDto(c.Id, c.Name, c.Description))
                .ToListAsync(ct)));

        group.MapPost("/", async (CreateCategoryRequest request, CatalogDbContext db, CancellationToken ct) =>
        {
            request.Validate();
            var name = request.Name.Trim();
            if (await db.Categories.AnyAsync(c => c.Name == name, ct))
                throw new ConflictException($"Category '{name}' already exists.");

            var category = new Category { Name = name, Description = request.Description };
            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);

            return TypedResults.Created($"/categories/{category.Id}", new CategoryDto(category.Id, category.Name, category.Description));
        });

        return app;
    }
}
