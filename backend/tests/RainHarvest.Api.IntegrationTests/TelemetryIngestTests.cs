using System.Net;
using System.Net.Http.Json;
using RainHarvest.Api.Features.Devices;
using RainHarvest.Api.Features.Telemetry;
using RainHarvest.Api.IntegrationTests.Infrastructure;
using RainHarvest.Contracts;
using static RainHarvest.Api.IntegrationTests.Infrastructure.TestHelpers;

namespace RainHarvest.Api.IntegrationTests;

/// <summary>M1: MQTT in → TimescaleDB → REST out, against real containers.</summary>
[Collection(ApiCollection.Name)]
public sealed class TelemetryIngestTests(ApiFixture fixture) : IAsyncLifetime
{
    private HttpClient _http = null!;
    private readonly TestDevice _device = new(fixture);

    public async Task InitializeAsync()
    {
        _http = await fixture.CreateAdminClientAsync();
        await _device.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        await _device.DisposeAsync();
        _http.Dispose();
    }

    [Fact]
    public async Task Published_telemetry_registers_the_device_and_becomes_the_latest_reading()
    {
        var deviceId = NewDeviceId();
        var sent = SampleTelemetry(levelPct: 64.2);

        await _device.PublishTelemetryAsync(deviceId, sent);

        var latest = await EventuallyAsync(() => TryGetAsync<ReadingDto>($"/api/v1/devices/{deviceId}/readings/latest"));
        Assert.Equal(64.2, latest.LevelPct);
        Assert.Equal(6.9, latest.Ph);
        Assert.Equal(18.4, latest.WaterTempC);
        Assert.True(latest.Raining);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(sent.Ts!.Value), latest.DeviceTs);
        Assert.InRange(latest.ReceivedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddSeconds(5));

        var devices = await _http.GetFromJsonAsync<List<DeviceDto>>("/api/v1/devices", Json);
        var device = Assert.Single(devices!, d => d.Id == deviceId);
        Assert.True(device.IsOnline);
        Assert.Equal(deviceId, device.Name);
        Assert.NotNull(device.LastSeenAt);
    }

    [Fact]
    public async Task Bad_messages_are_dropped_and_the_pipeline_keeps_working()
    {
        var deviceId = NewDeviceId();

        await _device.PublishRawAsync(deviceId, MqttTopics.Telemetry, "not json");
        await _device.PublishRawAsync(deviceId, MqttTopics.Telemetry, """{"levelPct":50}""");
        await _device.PublishTelemetryAsync(deviceId, SampleTelemetry(levelPct: 250)); // out of range
        await _device.PublishTelemetryAsync(deviceId, SampleTelemetry(levelPct: 42));

        var latest = await EventuallyAsync(() => TryGetAsync<ReadingDto>($"/api/v1/devices/{deviceId}/readings/latest"));
        Assert.Equal(42, latest.LevelPct);

        var page = await _http.GetFromJsonAsync<GetReadings.Response>($"/api/v1/devices/{deviceId}/readings", Json);
        Assert.Single(page!.Items);
    }

    [Fact]
    public async Task Status_messages_toggle_online_state()
    {
        var deviceId = NewDeviceId();

        await _device.PublishRawAsync(deviceId, MqttTopics.Status, DeviceStatus.Online);
        await EventuallyAsync(async () => (await TryGetAsync<GetDevice.Response>($"/api/v1/devices/{deviceId}"))
            is { Device.IsOnline: true } r ? r : null);

        await _device.PublishRawAsync(deviceId, MqttTopics.Status, DeviceStatus.Offline);
        var offline = await EventuallyAsync(async () => (await TryGetAsync<GetDevice.Response>($"/api/v1/devices/{deviceId}"))
            is { Device.IsOnline: false } r ? r : null);

        Assert.Null(offline.LatestReading);
    }

    [Fact]
    public async Task Unknown_device_returns_problem_details_404()
    {
        var response = await _http.GetAsync("/api/v1/devices/no-such-device/readings/latest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<T?> TryGetAsync<T>(string url) where T : class
    {
        var response = await _http.GetAsync(url);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>(Json) : null;
    }
}
