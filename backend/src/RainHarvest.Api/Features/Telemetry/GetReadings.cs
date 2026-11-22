using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Telemetry;

/// <summary>
/// GET /api/v1/devices/{deviceId}/readings?from&amp;to&amp;limit — raw samples, oldest first.
/// Window defaults to the last hour, at most 24 h. When more rows exist than
/// <c>limit</c>, <c>nextFrom</c> is set: call again with <c>from=nextFrom</c>.
/// For longer periods use <c>/readings/aggregate</c>.
/// </summary>
public sealed class GetReadings : IEndpoint
{
    public static readonly TimeSpan DefaultSpan = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxSpan = TimeSpan.FromHours(24);
    public const int DefaultLimit = 2000;
    public const int MaxLimit = 10_000;

    public sealed record Response(
        string DeviceId,
        DateTimeOffset From,
        DateTimeOffset To,
        IReadOnlyList<ReadingDto> Items,
        DateTimeOffset? NextFrom);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/devices/{deviceId}/readings", Handle)
            .WithName(nameof(GetReadings))
            .WithTags("Readings")
            .WithSummary("Raw readings in a time window (max 24 h)");

    private static async Task<Results<Ok<Response>, ValidationProblem, ProblemHttpResult>> Handle(
        string deviceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TimeRange.TryResolve(from, to, timeProvider.GetUtcNow(), DefaultSpan, MaxSpan, out var range, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = [$"'limit' must be between 1 and {MaxLimit}."],
            });
        }

        if (!await db.Devices.AnyAsync(d => d.Id == deviceId, cancellationToken))
        {
            return ApiProblems.DeviceNotFound(deviceId);
        }

        // Fetch one extra row to know whether another page exists.
        var rows = await db.Readings.AsNoTracking()
            .Where(r => r.DeviceId == deviceId && r.ReceivedAt >= range.From && r.ReceivedAt < range.To)
            .OrderBy(r => r.ReceivedAt)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        DateTimeOffset? nextFrom = null;
        if (rows.Count > take)
        {
            rows.RemoveAt(rows.Count - 1);
            // PostgreSQL stores microseconds; step past the last returned row.
            nextFrom = rows[^1].ReceivedAt.AddMicroseconds(1);
        }

        return TypedResults.Ok(new Response(deviceId, range.From, range.To, rows.Select(ReadingDto.From).ToList(), nextFrom));
    }
}
