using System.Threading.RateLimiting;
using BuildingBlocks;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Per-client fixed window limit; routes opt in via "RateLimiterPolicy": "per-client" in config.
var rateLimit = builder.Configuration.GetSection("RateLimiting");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("per-client", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimit.GetValue("PermitLimit", 100),
                Window = TimeSpan.FromSeconds(rateLimit.GetValue("WindowSeconds", 10)),
                QueueLimit = 0
            }));
});

var app = builder.Build();
app.UseServiceDefaults();

app.UseRateLimiter();
app.MapReverseProxy();
app.MapDefaultEndpoints();

app.Run();
