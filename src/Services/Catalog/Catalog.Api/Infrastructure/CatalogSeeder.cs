using Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Infrastructure;

public static class CatalogSeeder
{
    /// <summary>Applies pending migrations and inserts demo data when the catalog is empty.</summary>
    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        await db.Database.MigrateAsync();
        if (await db.Categories.AnyAsync())
            return;

        var phones = new Category { Name = "Phones", Description = "Smartphones" };
        var laptops = new Category { Name = "Laptops", Description = "Notebooks and ultrabooks" };
        var audio = new Category { Name = "Audio", Description = "Headphones and speakers" };

        db.Categories.AddRange(phones, laptops, audio);
        db.Products.AddRange(
            new Product { Name = "Pixel 10", Price = 799m, AvailableStock = 50, CategoryId = phones.Id },
            new Product { Name = "Galaxy S26", Price = 899m, AvailableStock = 40, CategoryId = phones.Id },
            new Product { Name = "ThinkPad X1 Carbon", Price = 1899m, AvailableStock = 15, CategoryId = laptops.Id },
            new Product { Name = "MacBook Air 15", Price = 1499m, AvailableStock = 20, CategoryId = laptops.Id },
            new Product { Name = "WH-1000XM6", Price = 399m, AvailableStock = 100, CategoryId = audio.Id },
            new Product { Name = "Bluetooth Speaker Mini", Price = 59m, AvailableStock = 0, CategoryId = audio.Id });

        await db.SaveChangesAsync();
    }
}
