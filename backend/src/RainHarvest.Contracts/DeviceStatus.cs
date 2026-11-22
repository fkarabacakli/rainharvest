namespace RainHarvest.Contracts;

/// <summary>
/// Plain-text body of <c>rainwater/{deviceId}/status</c> (retained, QoS 1).
/// The device publishes <see cref="Online"/> after connecting and registers
/// <see cref="Offline"/> as its MQTT Last Will.
/// </summary>
public static class DeviceStatus
{
    public const string Online = "online";
    public const string Offline = "offline";
}
