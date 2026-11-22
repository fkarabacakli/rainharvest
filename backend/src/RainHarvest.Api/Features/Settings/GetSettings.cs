using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Settings;

/// <summary>GET /api/v1/devices/{deviceId}/settings — effective thresholds (defaults if never set).</summary>
public sealed class GetSettings : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/devices/{deviceId}/settings", Handle)
            .WithName(nameof(GetSettings))
            .WithTags("Settings")
            .WithSummary("Device thresholds");

    private static async Task<Results<Ok<DeviceSettingsDto>, ProblemHttpResult>> Handle(
        string deviceId, AppDbContext db, DeviceSettingsService settings, CancellationToken cancellationToken)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == deviceId, cancellationToken))
        {
            return ApiProblems.DeviceNotFound(deviceId);
        }

        return TypedResults.Ok(DeviceSettingsDto.From(await settings.GetEffectiveAsync(deviceId, cancellationToken)));
    }
}
