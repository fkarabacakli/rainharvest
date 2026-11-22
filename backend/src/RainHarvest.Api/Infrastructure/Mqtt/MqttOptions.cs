using System.ComponentModel.DataAnnotations;

namespace RainHarvest.Api.Infrastructure.Mqtt;

public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    [Required]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 1883;

    /// <summary>true for HiveMQ Cloud (port 8883), false for local Mosquitto.</summary>
    public bool UseTls { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>Leave empty to generate a unique id per process (avoids two instances kicking each other off).</summary>
    public string? ClientId { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);
}
