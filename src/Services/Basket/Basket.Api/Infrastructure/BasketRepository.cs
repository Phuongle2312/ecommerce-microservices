using System.Text.Json;
using Basket.Api.Domain;
using StackExchange.Redis;

namespace Basket.Api.Infrastructure;

public interface IBasketRepository
{
    Task<ShoppingCart?> GetAsync(string userName);
    Task SaveAsync(ShoppingCart cart);
    Task<bool> DeleteAsync(string userName);
}

public sealed class RedisBasketRepository(IConnectionMultiplexer redis, IConfiguration configuration) : IBasketRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Abandoned carts expire instead of growing Redis forever.
    private readonly TimeSpan _ttl = TimeSpan.FromDays(configuration.GetValue("Basket:TtlDays", 30));
    private readonly IDatabase _db = redis.GetDatabase();

    private static RedisKey Key(string userName) => $"basket:{userName.ToLowerInvariant()}";

    public async Task<ShoppingCart?> GetAsync(string userName)
    {
        var value = await _db.StringGetAsync(Key(userName));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<ShoppingCart>(value.ToString(), JsonOptions);
    }

    public Task SaveAsync(ShoppingCart cart) =>
        _db.StringSetAsync(Key(cart.UserName), JsonSerializer.Serialize(cart, JsonOptions), _ttl);

    public Task<bool> DeleteAsync(string userName) => _db.KeyDeleteAsync(Key(userName));
}
