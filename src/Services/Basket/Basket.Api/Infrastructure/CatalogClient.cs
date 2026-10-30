using System.Globalization;
using BuildingBlocks.Contracts.Catalog;
using BuildingBlocks.Exceptions;
using Grpc.Core;

namespace Basket.Api.Infrastructure;

public sealed record CatalogProduct(Guid Id, string Name, decimal Price, int AvailableStock);

/// <summary>Thin wrapper over the generated gRPC client that maps transport errors to domain errors.</summary>
public sealed class CatalogClient(CatalogGrpc.CatalogGrpcClient client)
{
    public async Task<CatalogProduct> GetProductAsync(Guid productId, CancellationToken ct)
    {
        try
        {
            var reply = await client.GetProductAsync(new GetProductRequest { Id = productId.ToString() }, cancellationToken: ct);
            return new CatalogProduct(
                Guid.Parse(reply.Id),
                reply.Name,
                decimal.Parse(reply.Price, CultureInfo.InvariantCulture),
                reply.AvailableStock);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new NotFoundException("Product", productId);
        }
    }
}
