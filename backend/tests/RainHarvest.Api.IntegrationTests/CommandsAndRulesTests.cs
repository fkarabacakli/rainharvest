using System.Net;
using System.Net.Http.Json;
using Npgsql;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Features.Commands;
using RainHarvest.Api.Features.Devices;
using RainHarvest.Api.Features.Settings;
using RainHarvest.Api.IntegrationTests.Infrastructure;
using RainHarvest.Contracts;
using static RainHarvest.Api.IntegrationTests.Infrastructure.TestHelpers;

namespace RainHarvest.Api.IntegrationTests;

/// <summary>
/// M3: commands, device state, settings/config and the irrigation rule, with
/// the test playing the ESP32 on a real broker.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CommandsAndRulesTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);

    private HttpClient _admin = null!;
    private readonly TestDevice _device = new(fixture);
    private readonly string _id = NewDeviceId();

    private string CmdTopic => MqttTopics.For(_id, MqttTopics.Command);
    private string ConfigTopic => MqttTopics.For(_id, MqttTopics.Config);

    public async Task InitializeAsync()
    {
        _admin = await fixture.CreateAdminClientAsync();
        await _device.ConnectAsync();
        await _device.ListenAsync(_id);
    }

    public async Task DisposeAsync()
    {
        await _device.DisposeAsync();
        _admin.Dispose();
    }

    [Fact]
    public async Task Device_coming_online_receives_its_config()
    {
        await _device.PublishRawAsync(_id, MqttTopics.Status, DeviceStatus.Online);

        var config = await _device.ReceiveAsync<ConfigPayload>(ConfigTopic);

        var defaults = DeviceSettings.Defaults(_id);
        Assert.Equal(new ConfigPayload(defaults.PhMin, defaults.PhMax, defaults.TurbidityMaxNtu, defaults.LevelMinPct, defaults.PumpMaxS), config);
    }

    [Fact]
    public async Task Manual_irrigation_round_trip_follows_the_device_state()
    {
        await BringOnlineAsync();

        var response = await _admin.PostAsJsonAsync($"/api/v1/devices/{_id}/commands", new { action = "irrigate", durationS = 45 });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var sent = (await response.Content.ReadFromJsonAsync<CommandDto>(Json))!;
        Assert.Equal(CommandStatus.Sent, sent.Status);
        Assert.Equal(CommandSource.Manual, sent.Source);
        Assert.EndsWith($"/commands/{sent.Id}", response.Headers.Location!.ToString());

        var cmd = await _device.ReceiveAsync<CommandPayload>(CmdTopic);
        Assert.Equal(new CommandPayload(sent.Id.ToString(), CommandActions.Irrigate, 45), cmd);

        await _device.PublishStateAsync(_id, new StatePayload(true, true, true, cmd.CommandId, CommandResults.Accepted));
        await WaitForStatusAsync(sent.Id, CommandStatus.Accepted);
        var device = await _admin.GetFromJsonAsync<GetDevice.Response>($"/api/v1/devices/{_id}", Json);
        Assert.Equal(new ActuatorStateDto(true, true, true, device!.Device.Actuators.ReportedAt), device.Device.Actuators);

        await _device.PublishStateAsync(_id, new StatePayload(false, false, false, cmd.CommandId, CommandResults.Completed));
        await WaitForStatusAsync(sent.Id, CommandStatus.Completed);
    }

    [Fact]
    public async Task Device_rejection_is_recorded_with_its_reason()
    {
        await BringOnlineAsync();
        var sent = await SendAsync(new { action = "irrigate", durationS = 30 });
        var cmd = await _device.ReceiveAsync<CommandPayload>(CmdTopic);

        await _device.PublishStateAsync(_id, new StatePayload(false, false, false, cmd.CommandId, CommandResults.Rejected, RejectReasons.TankEmpty));

        var rejected = await WaitForStatusAsync(sent.Id, CommandStatus.Rejected);
        Assert.Equal(RejectReasons.TankEmpty, rejected.StatusReason);
    }

    [Fact]
    public async Task Invalid_or_undeliverable_commands_are_refused()
    {
        (await _admin.PostAsJsonAsync("/api/v1/devices", new { id = _id })).EnsureSuccessStatusCode(); // registered, never online

        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsJsonAsync($"/api/v1/devices/{_id}/commands", new { action = "stop" })).StatusCode);

        await BringOnlineAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync($"/api/v1/devices/{_id}/commands", new { action = "explode" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync($"/api/v1/devices/{_id}/commands", new { action = "irrigate" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync($"/api/v1/devices/{_id}/commands", new { action = "irrigate", durationS = 301 })).StatusCode);
        Assert.True(await _device.NothingOnAsync(CmdTopic, Quiet));
    }

    [Fact]
    public async Task Settings_are_validated_stored_and_pushed_to_the_device()
    {
        await BringOnlineAsync();
        var initial = await _admin.GetFromJsonAsync<DeviceSettingsDto>($"/api/v1/devices/{_id}/settings", Json);
        Assert.True(initial!.IsDefault);

        var invalid = await _admin.PutAsJsonAsync($"/api/v1/devices/{_id}/settings", Body(phMin: 8, phMax: 7));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var ok = await _admin.PutAsJsonAsync($"/api/v1/devices/{_id}/settings", Body(phMin: 6.5, phMax: 8));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True((await ok.Content.ReadFromJsonAsync<UpdateSettings.Response>(Json))!.ConfigPublished);

        var config = await _device.ReceiveAsync<ConfigPayload>(ConfigTopic);
        Assert.Equal((6.5, 8.0), (config.PhMin, config.PhMax));
        Assert.False((await _admin.GetFromJsonAsync<DeviceSettingsDto>($"/api/v1/devices/{_id}/settings", Json))!.IsDefault);
    }

    [Fact]
    public async Task Rule_irrigates_dry_soil_and_stops_its_own_irrigation_when_wet()
    {
        await BringOnlineAsync();

        await _device.PublishTelemetryAsync(_id, Telemetry(soil: 20));
        var irrigate = await _device.ReceiveAsync<CommandPayload>(CmdTopic);
        Assert.Equal(CommandActions.Irrigate, irrigate.Action);
        Assert.Equal(DeviceSettings.Defaults(_id).IrrigationDurationS, irrigate.DurationS);

        var history = await _admin.GetFromJsonAsync<List<CommandDto>>($"/api/v1/devices/{_id}/commands", Json);
        var auto = Assert.Single(history!);
        Assert.Equal(CommandSource.Rule, auto.Source);
        Assert.Equal("soil 20% < 30%", auto.Reason);

        await _device.PublishStateAsync(_id, new StatePayload(true, true, true, irrigate.CommandId, CommandResults.Accepted));
        await WaitForStatusAsync(auto.Id, CommandStatus.Accepted);

        await _device.PublishTelemetryAsync(_id, Telemetry(soil: 65));
        var stop = await _device.ReceiveAsync<CommandPayload>(CmdTopic);
        Assert.Equal(CommandActions.Stop, stop.Action);
    }

    [Fact]
    public async Task Rule_holds_back_on_bad_water_and_does_not_repeat_while_waiting()
    {
        await BringOnlineAsync();

        await _device.PublishTelemetryAsync(_id, Telemetry(soil: 20, ph: 5.0));
        Assert.True(await _device.NothingOnAsync(CmdTopic, Quiet), "bad water must not be used");

        await _device.PublishTelemetryAsync(_id, Telemetry(soil: 20));
        await _device.ReceiveAsync<CommandPayload>(CmdTopic);

        // No answer from the device yet: the next dry reading must not send another command.
        await _device.PublishTelemetryAsync(_id, Telemetry(soil: 19));
        Assert.True(await _device.NothingOnAsync(CmdTopic, Quiet), "a pending command must block the rule");
    }

    [Fact]
    public async Task Unanswered_commands_expire()
    {
        await BringOnlineAsync();
        var id = Guid.CreateVersion7();
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO device_commands (id, device_id, action, source, status, created_at, updated_at)
                VALUES (@id, @device, 'stop', 'Manual', 'Sent', now() - interval '1 minute', now() - interval '1 minute')
                """, connection);
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("device", _id);
            await insert.ExecuteNonQueryAsync();
        }

        var expired = await WaitForStatusAsync(id, CommandStatus.Expired);
        Assert.Equal("timeout", expired.StatusReason);
    }

    private async Task BringOnlineAsync()
    {
        await _device.PublishRawAsync(_id, MqttTopics.Status, DeviceStatus.Online);
        await _device.ReceiveAsync<ConfigPayload>(ConfigTopic); // proves the backend processed "online"
    }

    private async Task<CommandDto> SendAsync(object body)
    {
        var response = await _admin.PostAsJsonAsync($"/api/v1/devices/{_id}/commands", body);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CommandDto>(Json))!;
    }

    private Task<CommandDto> WaitForStatusAsync(Guid commandId, CommandStatus status) =>
        EventuallyAsync(async () =>
        {
            var command = await _admin.GetFromJsonAsync<CommandDto>($"/api/v1/devices/{_id}/commands/{commandId}", Json);
            return command!.Status == status ? command : null;
        });

    private static TelemetryPayload Telemetry(double soil, double ph = 7.0) =>
        new(LevelPct: 70, LevelCm: 56, TurbidityNtu: 5, Ph: ph, Raining: false, SoilPct: soil, FlowLpm: 0, FlowTotalL: 0, Rssi: -60);

    private static object Body(double phMin, double phMax) => new
    {
        phMin,
        phMax,
        turbidityMaxNtu = 25,
        levelMinPct = 15,
        soilDryPct = 30,
        soilWetPct = 60,
        irrigationDurationS = 60,
        irrigationCooldownS = 600,
        pumpMaxS = 300,
        autoIrrigation = true,
    };
}
