using System.Net;
using System.Net.Http.Json;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Features.Alerts;
using RainHarvest.Api.Features.Sites;
using RainHarvest.Api.IntegrationTests.Infrastructure;
using RainHarvest.Contracts;
using static RainHarvest.Api.IntegrationTests.Infrastructure.LiveListener;
using static RainHarvest.Api.IntegrationTests.Infrastructure.TestHelpers;

namespace RainHarvest.Api.IntegrationTests;

/// <summary>
/// M4 acceptance: bad water raises one alert, members see it live over SignalR
/// and in /alerts, outsiders get nothing; alerts resolve with hysteresis.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LiveAndAlertsTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1.5);

    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;
    private LiveListener _aliceLive = null!;
    private LiveListener _bobLive = null!;
    private LiveListener _adminLive = null!;
    private readonly TestDevice _device = new(fixture);
    private readonly string _id = NewDeviceId();

    public async Task InitializeAsync()
    {
        _admin = await fixture.CreateAdminClientAsync();
        (_alice, var alice) = await fixture.CreateUserClientAsync("alice");
        (_bob, var bob) = await fixture.CreateUserClientAsync("bob");

        var aliceSite = await CreateSiteAsync("Alice home");
        var bobSite = await CreateSiteAsync("Bob home");
        (await _admin.PutAsync($"/api/v1/sites/{aliceSite}/members/{alice.Id}", null)).EnsureSuccessStatusCode();
        (await _admin.PutAsync($"/api/v1/sites/{bobSite}/members/{bob.Id}", null)).EnsureSuccessStatusCode();
        (await _admin.PostAsJsonAsync("/api/v1/devices", new { id = _id, siteId = aliceSite })).EnsureSuccessStatusCode();

        // Connect after memberships exist: groups are joined on connect.
        _aliceLive = await LiveListener.ConnectAsync(fixture, Token(_alice));
        _bobLive = await LiveListener.ConnectAsync(fixture, Token(_bob));
        _adminLive = await LiveListener.ConnectAsync(fixture, Token(_admin));

        await _device.ConnectAsync();
        await _device.ListenAsync(_id);
        await _device.PublishRawAsync(_id, MqttTopics.Status, DeviceStatus.Online);
        await _device.ReceiveAsync<ConfigPayload>(MqttTopics.For(_id, MqttTopics.Config));
    }

    public async Task DisposeAsync()
    {
        await _device.DisposeAsync();
        await _aliceLive.DisposeAsync();
        await _bobLive.DisposeAsync();
        await _adminLive.DisposeAsync();
        _admin.Dispose();
        _alice.Dispose();
        _bob.Dispose();
    }

    [Fact]
    public async Task Alerts_query_filter_by_device_is_not_mistaken_for_a_device_route()
    {
        // Regression: the device access filter once treated ?deviceId= like {deviceId} and returned 404.
        Assert.Equal(HttpStatusCode.OK, (await _alice.GetAsync($"/api/v1/alerts?deviceId={_id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _bob.GetAsync($"/api/v1/alerts?deviceId={_id}")).StatusCode);
    }

    [Fact]
    public async Task Bad_water_raises_one_alert_for_members_and_admins_only()
    {
        await _device.PublishTelemetryAsync(_id, Telemetry(ph: 5.0));
        await _device.PublishTelemetryAsync(_id, Telemetry(ph: 5.1)); // still bad: must not open a second alert

        var raised = await _aliceLive.WaitAsync("AlertRaised", ForDevice(_id));
        Assert.Equal("WaterQualityBad", raised.GetProperty("type").GetString());
        Assert.Equal("Warning", raised.GetProperty("severity").GetString());
        await _adminLive.WaitAsync("AlertRaised", ForDevice(_id));
        Assert.True(await _bobLive.NothingAsync("AlertRaised", ForDevice(_id), Quiet), "an outsider received another site's alert");

        var active = await _alice.GetFromJsonAsync<List<AlertDto>>($"/api/v1/alerts?deviceId={_id}", Json);
        var alert = Assert.Single(active!);
        Assert.Equal(AlertType.WaterQualityBad, alert.Type);
        Assert.Null(alert.ResolvedAt);
        Assert.Empty((await _bob.GetFromJsonAsync<List<AlertDto>>($"/api/v1/alerts?deviceId={_id}", Json))!);
    }

    [Fact]
    public async Task Water_alert_resolves_only_once_clearly_back_in_range()
    {
        await _device.PublishTelemetryAsync(_id, Telemetry(ph: 5.0));
        await _aliceLive.WaitAsync("AlertRaised", ForDevice(_id));

        await _device.PublishTelemetryAsync(_id, Telemetry(ph: 6.05)); // in range, but inside the margin
        Assert.True(await _aliceLive.NothingAsync("AlertResolved", ForDevice(_id), Quiet));

        await _device.PublishTelemetryAsync(_id, Telemetry(ph: 7.0));
        var resolved = await _aliceLive.WaitAsync("AlertResolved", ForDevice(_id));
        Assert.NotEqual(JsonValueKindNull, resolved.GetProperty("resolvedAt").ValueKind);

        Assert.Empty((await _alice.GetFromJsonAsync<List<AlertDto>>($"/api/v1/alerts?deviceId={_id}", Json))!);
        Assert.Single((await _alice.GetFromJsonAsync<List<AlertDto>>($"/api/v1/alerts?deviceId={_id}&status=resolved", Json))!);
    }

    [Fact]
    public async Task Acknowledging_records_who_saw_it_and_is_limited_to_members()
    {
        await _device.PublishTelemetryAsync(_id, Telemetry(ph: 5.0));
        var raised = await _aliceLive.WaitAsync("AlertRaised", ForDevice(_id));
        var alertId = raised.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await _bob.PostAsync($"/api/v1/alerts/{alertId}/ack", null)).StatusCode);

        var ack = await _alice.PostAsync($"/api/v1/alerts/{alertId}/ack", null);
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);
        var acked = (await ack.Content.ReadFromJsonAsync<AlertDto>(Json))!;
        Assert.NotNull(acked.AcknowledgedAt);
        Assert.Null(acked.ResolvedAt); // seen, not fixed

        await _adminLive.WaitAsync("AlertAcknowledged", ForDevice(_id));
    }

    [Fact]
    public async Task Going_offline_raises_a_critical_alert_and_coming_back_resolves_it()
    {
        await _device.PublishRawAsync(_id, MqttTopics.Status, DeviceStatus.Offline);

        // Skip the "online" event from setup; wait for the transition to offline.
        await _aliceLive.WaitAsync("DeviceStatusChanged", e => ForDevice(_id)(e) && !e.GetProperty("isOnline").GetBoolean());
        var raised = await _aliceLive.WaitAsync("AlertRaised", ForDevice(_id));
        Assert.Equal("DeviceOffline", raised.GetProperty("type").GetString());
        Assert.Equal("Critical", raised.GetProperty("severity").GetString());

        await _device.PublishRawAsync(_id, MqttTopics.Status, DeviceStatus.Online);
        await _aliceLive.WaitAsync("AlertResolved", ForDevice(_id));
    }

    [Fact]
    public async Task Readings_commands_and_actuator_state_stream_to_members()
    {
        await _device.PublishTelemetryAsync(_id, Telemetry(ph: 7.0, soil: 50));
        var reading = await _aliceLive.WaitAsync("ReadingReceived", ForDevice(_id));
        Assert.Equal(50, reading.GetProperty("reading").GetProperty("soilPct").GetDouble());
        Assert.True(await _bobLive.NothingAsync("ReadingReceived", ForDevice(_id), Quiet));

        var response = await _alice.PostAsJsonAsync($"/api/v1/devices/{_id}/commands", new { action = "irrigate", durationS = 30 });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var sent = await _aliceLive.WaitAsync("CommandUpdated", ForDevice(_id));
        Assert.Equal("Sent", sent.GetProperty("status").GetString());

        var cmd = await _device.ReceiveAsync<CommandPayload>(MqttTopics.For(_id, MqttTopics.Command));
        await _device.PublishStateAsync(_id, new StatePayload(true, true, true, cmd.CommandId, CommandResults.Accepted));

        var actuators = await _aliceLive.WaitAsync("ActuatorStateChanged", ForDevice(_id));
        Assert.True(actuators.GetProperty("irrigating").GetBoolean());
        await _aliceLive.WaitAsync("CommandUpdated", e => ForDevice(_id)(e) && e.GetProperty("status").GetString() == "Accepted");
    }

    /// <summary>Browsers send the token as ?access_token= (no headers on WebSockets); only hub paths accept it.</summary>
    [Fact]
    public async Task Query_string_token_is_accepted_for_the_hub_only()
    {
        using var anonymous = fixture.CreateAnonymousClient();
        var token = Uri.EscapeDataString(Token(_alice));

        var withToken = await anonymous.PostAsync($"/hubs/live/negotiate?negotiateVersion=1&access_token={token}", null);
        var without = await anonymous.PostAsync("/hubs/live/negotiate?negotiateVersion=1", null);
        var apiWithQueryToken = await anonymous.GetAsync($"/api/v1/devices?access_token={token}");

        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, without.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, apiWithQueryToken.StatusCode); // tokens in URLs leak into logs; REST must not accept them
    }

    /// <summary>
    /// The SignalR JS client negotiates with credentials, so the panel's origin
    /// must get Allow-Credentials; unknown origins get no CORS headers at all.
    /// </summary>
    [Theory]
    [InlineData("/hubs/live/negotiate", "POST")]
    [InlineData("/api/v1/devices", "GET")]
    public async Task Cors_allows_the_panel_origin_with_credentials_and_nobody_else(string path, string method)
    {
        using var client = fixture.CreateAnonymousClient();

        async Task<HttpResponseMessage> PreflightAsync(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, path);
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", method);
            request.Headers.Add("Access-Control-Request-Headers", "authorization,x-requested-with,x-signalr-user-agent");
            return await client.SendAsync(request);
        }

        var panel = await PreflightAsync(ApiFixture.PanelOrigin);
        Assert.Equal([ApiFixture.PanelOrigin], panel.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.Equal(["true"], panel.Headers.GetValues("Access-Control-Allow-Credentials"));

        var stranger = await PreflightAsync("http://evil.example");
        Assert.False(stranger.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Hub_refuses_connections_without_a_valid_token()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => LiveListener.ConnectAsync(fixture, accessToken: null));
        await Assert.ThrowsAnyAsync<Exception>(() => LiveListener.ConnectAsync(fixture, accessToken: "not-a-jwt"));
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;

    private static string Token(HttpClient client) => client.DefaultRequestHeaders.Authorization!.Parameter!;

    private async Task<Guid> CreateSiteAsync(string name)
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/sites", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SiteDto>(Json))!.Id;
    }

    private static TelemetryPayload Telemetry(double ph, double soil = 50) =>
        new(LevelPct: 70, LevelCm: 56, TurbidityNtu: 5, Ph: ph, Raining: false, SoilPct: soil, FlowLpm: 0, FlowTotalL: 0, Rssi: -60);
}
