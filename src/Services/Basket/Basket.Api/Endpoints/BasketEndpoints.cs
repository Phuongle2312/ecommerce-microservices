using Basket.Api.Domain;
using Basket.Api.Infrastructure;
using BuildingBlocks.Exceptions;

namespace Basket.Api.Endpoints;

public sealed record SetItemRequest(Guid ProductId, int Quantity);

public sealed record CartDto(string UserName, IReadOnlyList<CartItemDto> Items, decimal TotalPrice, DateTime UpdatedAt)
{
    public static CartDto From(ShoppingCart cart) => new(
        cart.UserName,
        cart.Items.Select(i => new CartItemDto(i.ProductId, i.ProductName, i.Price, i.Quantity, i.Price * i.Quantity)).ToList(),
        cart.TotalPrice,
        cart.UpdatedAt);
}

public sealed record CartItemDto(Guid ProductId, string ProductName, decimal Price, int Quantity, decimal LineTotal);

public static class BasketEndpoints
{
    private const int MaxQuantityPerItem = 99;

    public static IEndpointRouteBuilder MapBasketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/baskets/{userName}").WithTags("Basket");

        group.MapGet("/", async (string userName, IBasketRepository repo) =>
        {
            var cart = await repo.GetAsync(userName) ?? new ShoppingCart { UserName = userName };
            return TypedResults.Ok(CartDto.From(cart));
        });

        // Adds or replaces an item. Price and stock always come from Catalog (via gRPC), never from the client.
        group.MapPut("/items", async (string userName, SetItemRequest request, IBasketRepository repo, CatalogClient catalog, CancellationToken ct) =>
        {
            if (request.ProductId == Guid.Empty || request.Quantity is < 1 or > MaxQuantityPerItem)
                throw new BadRequestException("Invalid basket item.", new Dictionary<string, string[]>
                {
                    ["Quantity"] = [$"Quantity must be between 1 and {MaxQuantityPerItem}; ProductId is required."]
                });

            var product = await catalog.GetProductAsync(request.ProductId, ct);
            if (product.AvailableStock < request.Quantity)
                throw new ConflictException($"Only {product.AvailableStock} of '{product.Name}' in stock.");

            var cart = await repo.GetAsync(userName) ?? new ShoppingCart { UserName = userName };
            cart.SetItem(product.Id, product.Name, product.Price, request.Quantity);
            await repo.SaveAsync(cart);

            return TypedResults.Ok(CartDto.From(cart));
        });

        group.MapDelete("/items/{productId:guid}", async (string userName, Guid productId, IBasketRepository repo) =>
        {
            var cart = await repo.GetAsync(userName) ?? throw new NotFoundException("Basket", userName);
            if (!cart.RemoveItem(productId)) throw new NotFoundException("Basket item", productId);
            await repo.SaveAsync(cart);
            return TypedResults.Ok(CartDto.From(cart));
        });

        group.MapDelete("/", async (string userName, IBasketRepository repo) =>
        {
            await repo.DeleteAsync(userName);
            return TypedResults.NoContent();
        });

        return app;
    }
}
