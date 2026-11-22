using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using RainHarvest.Api.Features.Telemetry;
using RainHarvest.Api.IntegrationTests.Infrastructure;
using static RainHarvest.Api.IntegrationTests.Infrastructure.TestHelpers;

namespace RainHarvest.Api.IntegrationTests;

/// <summary>Raw paging and TimescaleDB bucketing, on rows written straight to the database.</summary>
[Collection(ApiCollection.Name)]
public sealed class ReadingQueryTests(ApiFixture fixture) : IAsyncLifetime
{
    private HttpClient _http = null!;

    public async Task InitializeAsync() => _http = await fixture.CreateAdminClientAsync();

    public Task DisposeAsync()
    {
        _http.Dispose();
        return Task.CompletedTask;
    }

    // Recent, hour-aligned base time: inside the 90-day retention window and
    // newer than the aggregates' last refresh, so real-time aggregation is exercised.
    private static readonly DateTimeOffset Base = TruncateToHour(DateTimeOffset.UtcNow.AddHours(-3));

    [Fact]
    public async Task Raw_readings_page_with_nextFrom_until_exhausted()
    {
        var deviceId = NewDeviceId();
        await TestData.InsertReadingsAsync(fixture.ConnectionString, deviceId,
            Enumerable.Range(0, 5).Select(i => (Base.AddMinutes(i), (double)i)).ToArray());

        var levels = new List<double>();
        var from = Base;
        for (var pages = 1; ; pages++)
        {
            var page = await _http.GetFromJsonAsync<GetReadings.Response>(
                $"/api/v1/devices/{deviceId}/readings?from={Iso(from)}&to={Iso(Base.AddHours(1))}&limit=2", Json);

            levels.AddRange(page!.Items.Select(r => r.LevelPct));
            if (page.NextFrom is not { } next)
            {
                Assert.Equal(3, pages);
                break;
            }

            from = next;
        }

        Assert.Equal([0, 1, 2, 3, 4], levels);
    }

    [Theory]
    [InlineData("1m", 4, 65)]
    [InlineData("15m", 3, 60)]
    [InlineData("1h", 2, 60)]
    public async Task Aggregates_bucket_readings(string bucket, int expectedPoints, int lastBucketMinute)
    {
        var deviceId = NewDeviceId();
        await TestData.InsertReadingsAsync(fixture.ConnectionString, deviceId,
            (Base.AddMinutes(1), 10),
            (Base.AddMinutes(2), 20),
            (Base.AddMinutes(20), 30),
            (Base.AddMinutes(65), 40));
        await TestData.RefreshAggregatesAsync(fixture.ConnectionString, Base, Base.AddHours(2));

        var result = await _http.GetFromJsonAsync<GetReadingAggregates.Response>(
            $"/api/v1/devices/{deviceId}/readings/aggregate?bucket={bucket}&from={Iso(Base)}&to={Iso(Base.AddHours(2))}", Json);

        Assert.Equal(expectedPoints, result!.Points.Count);
        Assert.Equal(4, result.Points.Sum(p => p.Samples));
        Assert.Equal(Base.AddMinutes(lastBucketMinute), result.Points[^1].Time);

        var first = result.Points[0];
        switch (bucket)
        {
            case "15m":
                Assert.Equal(Base, first.Time);
                Assert.Equal(new GetReadingAggregates.Stat(15, 10, 20), first.LevelPct);
                break;
            case "1h":
                Assert.Equal(Base, first.Time);
                Assert.Equal(new GetReadingAggregates.Stat(20, 10, 30), first.LevelPct);
                break;
        }
    }

    [Theory]
    [InlineData("readings/aggregate?bucket=5m", "bucket")]
    [InlineData("readings?from=2026-11-22T12:00:00Z&to=2026-11-22T11:00:00Z", "from")]
    [InlineData("readings?from=2026-11-01T00:00:00Z&to=2026-11-03T00:00:00Z", "from")]
    [InlineData("readings?limit=0", "limit")]
    [InlineData("readings/aggregate?bucket=1m&from=2026-01-01T00:00:00Z&to=2026-02-01T00:00:00Z", "from")]
    public async Task Invalid_queries_return_validation_problems(string query, string field)
    {
        var deviceId = NewDeviceId();
        await TestData.InsertReadingsAsync(fixture.ConnectionString, deviceId);

        var response = await _http.GetAsync($"/api/v1/devices/{deviceId}/{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Continuous_aggregates_and_policies_are_installed()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        Assert.Equal(["readings_15m", "readings_1h"], await ListAsync(connection,
            "SELECT view_name FROM timescaledb_information.continuous_aggregates ORDER BY view_name"));

        var jobs = await ListAsync(connection,
            "SELECT proc_name || ':' || hypertable_name FROM timescaledb_information.jobs WHERE hypertable_name IS NOT NULL ORDER BY 1");
        Assert.Contains("policy_retention:readings", jobs);
        Assert.Contains(jobs, j => j.StartsWith("policy_compression:readings", StringComparison.Ordinal)
                                   || j.StartsWith("policy_columnstore:readings", StringComparison.Ordinal));
        Assert.Equal(2, jobs.Count(j => j.StartsWith("policy_refresh_continuous_aggregate:", StringComparison.Ordinal)));
    }

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static string Iso(DateTimeOffset value) => Uri.EscapeDataString(value.UtcDateTime.ToString("O"));

    private static DateTimeOffset TruncateToHour(DateTimeOffset t) => new(t.Year, t.Month, t.Day, t.Hour, 0, 0, TimeSpan.Zero);
}
