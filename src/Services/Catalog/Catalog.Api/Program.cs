using BuildingBlocks;
using Catalog.Api.Endpoints;
using Catalog.Api.Grpc;
using Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("CatalogDb")
                       ?? throw new InvalidOperationException("Connection string 'CatalogDb' is missing.");

builder.Services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddGrpc();
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres");

var app = builder.Build();
app.UseServiceDefaults();

if (app.Environment.IsDevelopment())
    await app.MigrateAndSeedAsync();

app.MapProductEndpoints();
app.MapCategoryEndpoints();
app.MapGrpcService<CatalogGrpcService>();
app.MapDefaultEndpoints();

app.Run();
