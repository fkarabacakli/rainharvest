using System.Globalization;

namespace RainHarvest.DeviceSimulator;

/// <summary>Command-line options. Defaults match the local docker-compose broker.</summary>
public sealed record SimulatorOptions
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 1883;
    public bool UseTls { get; init; }
    public string? Username { get; init; } = "simulator";
    public string? Password { get; init; } = "simulator-dev";
    public IReadOnlyList<string> Devices { get; init; } = ["node1"];
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Simulated seconds per real second; 60 makes one real minute an hour of weather.</summary>
    public double Speed { get; init; } = 1;

    public string Scenario { get; init; } = "normal";
    public int? Seed { get; init; }

    public const string Usage =
        """
        RainHarvest device simulator — behaves like one or more ESP32 nodes over MQTT.

        Options:
          --host <host>          Broker host            (default: localhost)
          --port <port>          Broker port            (default: 1883)
          --tls                  Use TLS (HiveMQ Cloud, port 8883)
          --user <name>          MQTT username          (default: simulator)
          --password <secret>    MQTT password          (default: simulator-dev)
          --devices <a,b,...>    Device ids             (default: node1)
          --interval <seconds>   Telemetry interval     (default: 10)
          --speed <factor>       Simulated time speed   (default: 1)
          --scenario <name>      normal | rain | drought | bad-water | flaky (default: normal)
          --seed <int>           Random seed for repeatable runs
          --help                 Show this help
        """;

    public static SimulatorOptions Parse(string[] args)
    {
        var options = new SimulatorOptions();

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length
                ? args[++i]
                : throw new ArgumentException($"Missing value for {args[i]}.");

            options = args[i] switch
            {
                "--host" => options with { Host = Next() },
                "--port" => options with { Port = int.Parse(Next(), CultureInfo.InvariantCulture) },
                "--tls" => options with { UseTls = true },
                "--user" => options with { Username = Next() },
                "--password" => options with { Password = Next() },
                "--devices" => options with
                {
                    Devices = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                },
                "--interval" => options with
                {
                    Interval = TimeSpan.FromSeconds(double.Parse(Next(), CultureInfo.InvariantCulture)),
                },
                "--speed" => options with { Speed = double.Parse(Next(), CultureInfo.InvariantCulture) },
                "--scenario" => options with { Scenario = Next() },
                "--seed" => options with { Seed = int.Parse(Next(), CultureInfo.InvariantCulture) },
                _ => throw new ArgumentException($"Unknown option '{args[i]}'."),
            };
        }

        if (options.Devices.Count == 0)
        {
            throw new ArgumentException("--devices needs at least one id.");
        }

        if (options.Interval <= TimeSpan.Zero || options.Speed <= 0)
        {
            throw new ArgumentException("--interval and --speed must be positive.");
        }

        if (!DeviceSimulator.Scenario.All.ContainsKey(options.Scenario))
        {
            throw new ArgumentException(
                $"Unknown scenario '{options.Scenario}'. Available: {string.Join(", ", DeviceSimulator.Scenario.All.Keys)}.");
        }

        return options;
    }
}
