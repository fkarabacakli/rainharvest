namespace RainHarvest.Contracts;

/// <summary>
/// MQTT topic names shared by the backend and the device simulator.
/// Layout: <c>rainwater/{deviceId}/{channel}</c>. See docs/mqtt.md.
/// </summary>
public static class MqttTopics
{
    public const string Root = "rainwater";

    public const string Telemetry = "telemetry";
    public const string State = "state";
    public const string Status = "status";
    public const string Command = "cmd";
    public const string Config = "config";

    /// <summary>Topic for one device and channel, e.g. <c>rainwater/node1/telemetry</c>.</summary>
    public static string For(string deviceId, string channel) => $"{Root}/{deviceId}/{channel}";

    /// <summary>Wildcard filter for one channel across all devices, e.g. <c>rainwater/+/telemetry</c>.</summary>
    public static string AllDevices(string channel) => $"{Root}/+/{channel}";

    /// <summary>Extracts the device id from a topic in the <c>rainwater/{deviceId}/...</c> layout.</summary>
    public static bool TryGetDeviceId(string topic, out string deviceId)
    {
        var parts = topic.Split('/');
        if (parts.Length >= 3 && parts[0] == Root && parts[1].Length > 0)
        {
            deviceId = parts[1];
            return true;
        }

        deviceId = string.Empty;
        return false;
    }
}
