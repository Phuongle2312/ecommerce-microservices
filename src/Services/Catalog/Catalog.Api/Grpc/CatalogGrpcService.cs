using System.Globalization;
using BuildingBlocks.Contracts.Catalog;
using Catalog.Api.Infrastructure;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Grpc;

/// <summary>Internal API used by other services (e.g. Basket) to read authoritative price and stock.</summary>
public sealed class CatalogGrpcService(CatalogDbContext db) : CatalogGrpc.CatalogGrpcBase
{
    public override async Task<ProductReply> GetProduct(GetProductRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"'{request.Id}' is not a valid product id."));

        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.Id, p.Name, p.Price, p.AvailableStock })
            .FirstOrDefaultAsync(context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Product '{id}' was not found."));

        return ToReply(product.Id, product.Name, product.Price, product.AvailableStock);
    }

    public override async Task<GetProductsReply> GetProducts(GetProductsRequest request, ServerCallContext context)
    {
        var ids = request.Ids.Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();

        var products = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Price, p.AvailableStock })
            .ToListAsync(context.CancellationToken);

        var reply = new GetProductsReply();
        reply.Products.AddRange(products.Select(p => ToReply(p.Id, p.Name, p.Price, p.AvailableStock)));
        return reply;
    }

    private static ProductReply ToReply(Guid id, string name, decimal price, int stock) => new()
    {
        Id = id.ToString(),
        Name = name,
        Price = price.ToString(CultureInfo.InvariantCulture),
        AvailableStock = stock
    };
}
