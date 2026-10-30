using BuildingBlocks.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

namespace BuildingBlocks;

/// <summary>Cross-cutting setup shared by every service: logging, error handling, health checks.</summary>
public static class ServiceDefaults
{
    private const string LiveTag = "live";

    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
        var serviceName = builder.Environment.ApplicationName;

        builder.Services.AddSerilog((_, lc) => lc
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {SourceContext}: {Message:lj}{NewLine}{Exception}"));

        builder.Services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Instance = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
                ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
            });
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        // "self" is the liveness check; dependency checks added by each service make up readiness.
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        return builder;
    }

    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        // Request logging wraps the exception handler so it records the final status code, not the raw exception.
        app.UseSerilogRequestLogging();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains(LiveTag) });
        app.MapHealthChecks("/health/ready");
        return app;
    }
}
