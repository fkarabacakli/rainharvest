using System.Text.Json;
using RainHarvest.Contracts;

namespace RainHarvest.Api.UnitTests.Telemetry;

/// <summary>Pins the JSON contract the ESP32 firmware has to follow (docs/mqtt.md).</summary>
public sealed class TelemetryPayloadJsonTests
{
    [Fact]
    public void Parses_the_documented_example()
    {
        const string json =
            """
            {"ts":1763800000,"levelPct":64.2,"levelCm":38.5,"turbidityNtu":12.0,"ph":6.9,
             "waterTempC":18.4,"raining":true,"soilPct":31,"flowLpm":0.0,"flowTotalL":142.7,"rssi":-61}
            """;

        var payload = JsonSerializer.Deserialize<TelemetryPayload>(json, MqttJson.Options);

        Assert.Equal(
            new TelemetryPayload(64.2, 38.5, 12.0, 6.9, true, 31, 0, 142.7, -61, 1763800000, 18.4),
            payload);
    }

    [Fact]
    public void Optional_fields_may_be_omitted()
    {
        const string json =
            """{"levelPct":1,"levelCm":1,"turbidityNtu":1,"ph":7,"raining":false,"soilPct":1,"flowLpm":0,"flowTotalL":0,"rssi":-50}""";

        var payload = JsonSerializer.Deserialize<TelemetryPayload>(json, MqttJson.Options)!;

        Assert.Null(payload.Ts);
        Assert.Null(payload.WaterTempC);
    }

    [Fact]
    public void Unknown_fields_are_ignored_for_forward_compatibility()
    {
        const string json =
            """{"levelPct":1,"levelCm":1,"turbidityNtu":1,"ph":7,"raining":false,"soilPct":1,"flowLpm":0,"flowTotalL":0,"rssi":-50,"fw":"1.2.0"}""";

        Assert.NotNull(JsonSerializer.Deserialize<TelemetryPayload>(json, MqttJson.Options));
    }

    [Theory]
    [InlineData("""{"levelCm":1,"turbidityNtu":1,"ph":7,"raining":false,"soilPct":1,"flowLpm":0,"flowTotalL":0,"rssi":-50}""")]
    [InlineData("""{"levelPct":null,"levelCm":1,"turbidityNtu":1,"ph":7,"raining":false,"soilPct":1,"flowLpm":0,"flowTotalL":0,"rssi":-50}""")]
    [InlineData("""{"levelPct":"high","levelCm":1,"turbidityNtu":1,"ph":7,"raining":false,"soilPct":1,"flowLpm":0,"flowTotalL":0,"rssi":-50}""")]
    public void Missing_null_or_mistyped_required_fields_are_rejected(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TelemetryPayload>(json, MqttJson.Options));
}
