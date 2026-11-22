using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Features.Telemetry;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Devices;

/// <summary>GET /api/v1/devices/{deviceId} — device plus its latest reading (null if none yet).</summary>
public sealed class GetDevice : IEndpoint
{
    public sealed record Response(DeviceDto Device, ReadingDto? LatestReading);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/devices/{deviceId}", Handle)
            .WithName(nameof(GetDevice))
            .WithTags("Devices")
            .WithSummary("Device detail with latest reading");

    private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
        string deviceId, AppDbContext db, CancellationToken cancellationToken)
    {
        var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deviceId, cancellationToken);
        if (device is null)
        {
            return ApiProblems.DeviceNotFound(deviceId);
        }

        var latest = await db.Readings.AsNoTracking()
            .Where(r => r.DeviceId == deviceId)
            .OrderByDescending(r => r.ReceivedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return TypedResults.Ok(new Response(DeviceDto.From(device), latest is null ? null : ReadingDto.From(latest)));
    }
}
