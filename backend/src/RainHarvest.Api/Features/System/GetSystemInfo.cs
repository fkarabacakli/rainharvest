using System.Reflection;
using RainHarvest.Api.Common.Endpoints;

namespace RainHarvest.Api.Features.System;

/// <summary>GET /api/v1/system/info — lets clients check they reach the right backend.</summary>
public sealed class GetSystemInfo : IEndpoint
{
    public sealed record Response(string Name, string Version, string Environment, DateTimeOffset ServerTime);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/system/info", Handle)
            .WithName(nameof(GetSystemInfo))
            .WithTags("System")
            .WithSummary("Backend name, version and server time")
            .AllowAnonymous();

    private static Response Handle(IHostEnvironment environment, TimeProvider timeProvider)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        return new Response("RainHarvest", version, environment.EnvironmentName, timeProvider.GetUtcNow());
    }
}
