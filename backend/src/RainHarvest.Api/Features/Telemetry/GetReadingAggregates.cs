using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Telemetry;

/// <summary>
/// GET /api/v1/devices/{deviceId}/readings/aggregate?bucket=1m|15m|1h&amp;from&amp;to
/// Time-bucketed statistics for charts. 15m and 1h come from TimescaleDB
/// continuous aggregates (real-time, so the newest bucket is included);
/// 1m is computed from raw readings on the fly.
/// </summary>
public sealed class GetReadingAggregates : IEndpoint
{
    public sealed record Stat(double Avg, double Min, double Max);

    public sealed record Point(
        DateTimeOffset Time,
        long Samples,
        Stat LevelPct,
        Stat Ph,
        Stat TurbidityNtu,
        Stat SoilPct,
        double? WaterTempCAvg,
        double FlowLpmAvg,
        double FlowLpmMax,
        double FlowTotalLMax,
        bool Raining,
        double RssiAvg);

    public sealed record Response(string DeviceId, string Bucket, DateTimeOffset From, DateTimeOffset To, IReadOnlyList<Point> Points);

    /// <summary>Allowed buckets: PostgreSQL interval, default window, maximum window, source.</summary>
    public static readonly IReadOnlyDictionary<string, BucketSpec> Buckets = new Dictionary<string, BucketSpec>
    {
        ["1m"] = new("1 minute", TimeSpan.FromHours(6), TimeSpan.FromDays(7), Source: null),
        ["15m"] = new("15 minutes", TimeSpan.FromDays(7), TimeSpan.FromDays(90), Source: "readings_15m"),
        ["1h"] = new("1 hour", TimeSpan.FromDays(30), TimeSpan.FromDays(400), Source: "readings_1h"),
    };

    public sealed record BucketSpec(string Interval, TimeSpan DefaultSpan, TimeSpan MaxSpan, string? Source);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/devices/{deviceId}/readings/aggregate", Handle)
            .WithName(nameof(GetReadingAggregates))
            .WithTags("Readings")
            .WithSummary("Bucketed reading statistics (bucket = 1m, 15m or 1h)");

    private static async Task<Results<Ok<Response>, ValidationProblem, ProblemHttpResult>> Handle(
        string deviceId,
        string? bucket,
        DateTimeOffset? from,
        DateTimeOffset? to,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var bucketKey = bucket ?? "15m";
        if (!Buckets.TryGetValue(bucketKey, out var spec))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bucket"] = [$"'bucket' must be one of: {string.Join(", ", Buckets.Keys)}."],
            });
        }

        if (!TimeRange.TryResolve(from, to, timeProvider.GetUtcNow(), spec.DefaultSpan, spec.MaxSpan, out var range, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!await db.Devices.AnyAsync(d => d.Id == deviceId, cancellationToken))
        {
            return ApiProblems.DeviceNotFound(deviceId);
        }

        var rows = await db.Database
            .SqlQueryRaw<AggregateRow>(BuildSql(spec), deviceId, range.From, range.To)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new Response(deviceId, bucketKey, range.From, range.To, rows.Select(ToPoint).ToList()));
    }

    // Interval and view names come only from the Buckets table above, never from user input.
    private static string BuildSql(BucketSpec spec) => spec.Source is { } view
        ? $$"""
          SELECT {{Columns}}
          FROM {{view}}
          WHERE device_id = {0}
            AND bucket >= time_bucket(INTERVAL '{{spec.Interval}}', {1})
            AND bucket < {2}
          ORDER BY bucket
          """
        : $$"""
          SELECT {{Columns}}
          FROM (
              SELECT time_bucket(INTERVAL '{{spec.Interval}}', received_at) AS bucket,
                     count(*)                    AS sample_count,
                     avg(level_pct)              AS level_pct_avg,
                     min(level_pct)              AS level_pct_min,
                     max(level_pct)              AS level_pct_max,
                     avg(ph)                     AS ph_avg,
                     min(ph)                     AS ph_min,
                     max(ph)                     AS ph_max,
                     avg(turbidity_ntu)          AS turbidity_ntu_avg,
                     min(turbidity_ntu)          AS turbidity_ntu_min,
                     max(turbidity_ntu)          AS turbidity_ntu_max,
                     avg(soil_pct)               AS soil_pct_avg,
                     min(soil_pct)               AS soil_pct_min,
                     max(soil_pct)               AS soil_pct_max,
                     avg(water_temp_c)           AS water_temp_c_avg,
                     avg(flow_lpm)               AS flow_lpm_avg,
                     max(flow_lpm)               AS flow_lpm_max,
                     max(flow_total_l)           AS flow_total_l_max,
                     bool_or(raining)            AS raining,
                     avg(rssi)::double precision AS rssi_avg
              FROM readings
              WHERE device_id = {0}
                AND received_at >= time_bucket(INTERVAL '{{spec.Interval}}', {1})
                AND received_at < {2}
              GROUP BY bucket
          ) b
          ORDER BY bucket
          """;

    private const string Columns =
        "bucket, sample_count, level_pct_avg, level_pct_min, level_pct_max, ph_avg, ph_min, ph_max, " +
        "turbidity_ntu_avg, turbidity_ntu_min, turbidity_ntu_max, soil_pct_avg, soil_pct_min, soil_pct_max, " +
        "water_temp_c_avg, flow_lpm_avg, flow_lpm_max, flow_total_l_max, raining, rssi_avg";

    private static Point ToPoint(AggregateRow r) => new(
        r.Bucket,
        r.SampleCount,
        new Stat(r.LevelPctAvg, r.LevelPctMin, r.LevelPctMax),
        new Stat(r.PhAvg, r.PhMin, r.PhMax),
        new Stat(r.TurbidityNtuAvg, r.TurbidityNtuMin, r.TurbidityNtuMax),
        new Stat(r.SoilPctAvg, r.SoilPctMin, r.SoilPctMax),
        r.WaterTempCAvg,
        r.FlowLpmAvg,
        r.FlowLpmMax,
        r.FlowTotalLMax,
        r.Raining,
        r.RssiAvg);

    /// <summary>Row shape of the SQL above; columns map by snake_case name.</summary>
    public sealed class AggregateRow
    {
        public DateTimeOffset Bucket { get; init; }
        public long SampleCount { get; init; }
        public double LevelPctAvg { get; init; }
        public double LevelPctMin { get; init; }
        public double LevelPctMax { get; init; }
        public double PhAvg { get; init; }
        public double PhMin { get; init; }
        public double PhMax { get; init; }
        public double TurbidityNtuAvg { get; init; }
        public double TurbidityNtuMin { get; init; }
        public double TurbidityNtuMax { get; init; }
        public double SoilPctAvg { get; init; }
        public double SoilPctMin { get; init; }
        public double SoilPctMax { get; init; }
        public double? WaterTempCAvg { get; init; }
        public double FlowLpmAvg { get; init; }
        public double FlowLpmMax { get; init; }
        public double FlowTotalLMax { get; init; }
        public bool Raining { get; init; }
        public double RssiAvg { get; init; }
    }
}
