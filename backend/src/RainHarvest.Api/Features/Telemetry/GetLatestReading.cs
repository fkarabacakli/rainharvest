using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Telemetry;

/// <summary>GET /api/v1/devices/{deviceId}/readings/latest</summary>
public sealed class GetLatestReading : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/devices/{deviceId}/readings/latest", Handle)
            .WithName(nameof(GetLatestReading))
            .WithTags("Readings")
            .WithSummary("Most recent reading of a device");

    private static async Task<Results<Ok<ReadingDto>, ProblemHttpResult>> Handle(
        string deviceId, AppDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == deviceId, cancellationToken))
        {
            return ApiProblems.DeviceNotFound(deviceId);
        }

        var latest = await db.Readings.AsNoTracking()
            .Where(r => r.DeviceId == deviceId)
            .OrderByDescending(r => r.ReceivedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return latest is null ? ApiProblems.NoReadings(deviceId) : TypedResults.Ok(ReadingDto.From(latest));
    }
}
