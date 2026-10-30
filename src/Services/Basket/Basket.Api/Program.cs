using Basket.Api.Endpoints;
using Basket.Api.Infrastructure;
using BuildingBlocks;
using BuildingBlocks.Contracts.Catalog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var redisConnection = builder.Configuration.GetConnectionString("Redis")
                      ?? throw new InvalidOperationException("Connection string 'Redis' is missing.");
var catalogGrpcUrl = builder.Configuration["Services:CatalogGrpc"]
                     ?? throw new InvalidOperationException("'Services:CatalogGrpc' is missing.");

builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddScoped<IBasketRepository, RedisBasketRepository>();

builder.Services.AddGrpcClient<CatalogGrpc.CatalogGrpcClient>(o => o.Address = new Uri(catalogGrpcUrl));
builder.Services.AddScoped<CatalogClient>();

builder.Services.AddHealthChecks().AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), name: "redis");

var app = builder.Build();
app.UseServiceDefaults();

app.MapBasketEndpoints();
app.MapDefaultEndpoints();

app.Run();
