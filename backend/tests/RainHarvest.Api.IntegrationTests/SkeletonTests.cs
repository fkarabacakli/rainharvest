using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using RainHarvest.Api.IntegrationTests.Infrastructure;

namespace RainHarvest.Api.IntegrationTests;

/// <summary>M0 acceptance: schema migrated, readings is a hypertable, health is green.</summary>
[Collection(ApiCollection.Name)]
public sealed class SkeletonTests(ApiFixture fixture)
{
    [Fact]
    public async Task Readings_table_is_a_timescale_hypertable()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM timescaledb_information.hypertables WHERE hypertable_name = 'readings'",
            connection);

        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Health_is_healthy_once_database_and_broker_are_connected()
    {
        using var client = fixture.Factory.CreateClient();

        // The MQTT worker connects in the background; give it a few seconds.
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            response = await client.GetAsync("/health");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(500);
        }

        Assert.NotNull(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());

        var checks = body.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        Assert.Equal("Healthy", checks["database"]);
        Assert.Equal("Healthy", checks["mqtt"]);
    }

    [Fact]
    public async Task System_info_is_served_under_api_v1()
    {
        using var client = fixture.Factory.CreateClient();

        var info = await client.GetFromJsonAsync<JsonElement>("/api/v1/system/info");

        Assert.Equal("RainHarvest", info.GetProperty("name").GetString());
        Assert.Equal("Testing", info.GetProperty("environment").GetString());
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        using var client = await fixture.CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
