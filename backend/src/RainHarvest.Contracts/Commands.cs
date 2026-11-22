namespace RainHarvest.Contracts;

/// <summary>
/// Body of <c>rainwater/{deviceId}/cmd</c> (backend → device, QoS 1, not retained).
/// </summary>
/// <param name="CommandId">Unique id; the device echoes it in <see cref="StatePayload.LastCommandId"/>.</param>
/// <param name="Action">One of <see cref="CommandActions"/>.</param>
/// <param name="DurationS">Seconds; required for <c>irrigate</c> and <c>pump_on</c>, ignored otherwise.</param>
public sealed record CommandPayload(string CommandId, string Action, int? DurationS = null);

/// <summary>Allowed <see cref="CommandPayload.Action"/> values.</summary>
public static class CommandActions
{
    /// <summary>Full sequence run by the device: open valve → pump on → wait → pump off → close valve.</summary>
    public const string Irrigate = "irrigate";

    /// <summary>Abort irrigation or any running pump, then close the valve.</summary>
    public const string Stop = "stop";

    // Maintenance commands; the device still enforces the pump/valve interlock.
    public const string PumpOn = "pump_on";
    public const string PumpOff = "pump_off";
    public const string ValveOpen = "valve_open";
    public const string ValveClose = "valve_close";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Irrigate, Stop, PumpOn, PumpOff, ValveOpen, ValveClose };

    public static bool NeedsDuration(string action) => action is Irrigate or PumpOn;
}

/// <summary>
/// Body of <c>rainwater/{deviceId}/state</c> (device → backend, QoS 1, retained).
/// Published on every change. The device is the source of truth for actuator state.
/// </summary>
/// <param name="PumpOn">Pump relay energised.</param>
/// <param name="ValveOpen">Normally-closed valve energised (open).</param>
/// <param name="Irrigating">An <c>irrigate</c> sequence is running.</param>
/// <param name="LastCommandId">The command this state change answers, if any.</param>
/// <param name="LastCommandResult">One of <see cref="CommandResults"/>.</param>
/// <param name="Reason">For <c>rejected</c>: one of <see cref="RejectReasons"/>.</param>
public sealed record StatePayload(
    bool PumpOn,
    bool ValveOpen,
    bool Irrigating,
    string? LastCommandId = null,
    string? LastCommandResult = null,
    string? Reason = null);

public static class CommandResults
{
    /// <summary>Device started executing the command.</summary>
    public const string Accepted = "accepted";

    /// <summary>Device refused; see <see cref="StatePayload.Reason"/>.</summary>
    public const string Rejected = "rejected";

    /// <summary>A timed command (irrigate, pump_on) finished or was stopped.</summary>
    public const string Completed = "completed";
}

public static class RejectReasons
{
    public const string TankEmpty = "tank_empty";
    public const string WaterQuality = "water_quality";
    public const string ValveClosed = "valve_closed";
    public const string PumpRunning = "pump_running";
    public const string Busy = "busy";
    public const string Invalid = "invalid";
}

/// <summary>
/// Body of <c>rainwater/{deviceId}/config</c> (backend → device, QoS 1, retained).
/// The device's own safety limits; it applies them even when the backend is unreachable.
/// </summary>
public sealed record ConfigPayload(
    double PhMin,
    double PhMax,
    double TurbidityMaxNtu,
    double LevelMinPct,
    int PumpMaxS);
