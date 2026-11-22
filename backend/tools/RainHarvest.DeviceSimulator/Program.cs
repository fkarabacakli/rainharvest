using System.Runtime.InteropServices;
using RainHarvest.DeviceSimulator;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(SimulatorOptions.Usage);
    return 0;
}

SimulatorOptions options;
try
{
    options = SimulatorOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(SimulatorOptions.Usage);
    return 2;
}

using var stopping = new CancellationTokenSource();

// Ctrl+C and `docker stop` (SIGTERM): cancel the default exit so every device
// can publish "offline" first. Declared after the CTS, so disposed before it.
using var onSigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
using var onSigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);

void Stop(PosixSignalContext context)
{
    context.Cancel = true;
    stopping.Cancel();
}

Console.WriteLine(
    $"Simulating {string.Join(", ", options.Devices)} ({options.Scenario}) against {options.Host}:{options.Port}, " +
    $"every {options.Interval.TotalSeconds:0.#}s at {options.Speed}x. Ctrl+C to stop.");

var baseSeed = options.Seed ?? Environment.TickCount;
var scenario = Scenario.All[options.Scenario];
Console.WriteLine($"Scenario '{scenario.Name}': {scenario.Description}.");
var devices = options.Devices.Select((id, i) => new SimulatedDevice(id, options, scenario, baseSeed + i)).ToList();
try
{
    await Task.WhenAll(devices.Select(d => d.RunAsync(stopping.Token)));
}
finally
{
    devices.ForEach(d => d.Dispose());
}

return 0;
