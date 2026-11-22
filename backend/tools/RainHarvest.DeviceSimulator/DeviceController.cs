using RainHarvest.Contracts;

namespace RainHarvest.DeviceSimulator;

/// <summary>
/// What the ESP32 firmware is expected to do (docs/mqtt.md): run the
/// irrigation sequence, enforce the pump/valve interlock and the safety limits
/// from <c>config</c>, and report every change as <c>state</c>. Not thread-safe;
/// the caller serialises access.
/// </summary>
public sealed class DeviceController
{
    private TimeSpan _pumpUntil;
    private TimeSpan _pumpStartedAt;
    private string? _activeCommandId;

    /// <summary>Firmware defaults, used until the backend sends a config.</summary>
    public ConfigPayload Config { get; set; } = new(PhMin: 6.0, PhMax: 8.5, TurbidityMaxNtu: 25, LevelMinPct: 15, PumpMaxS: 300);

    public bool PumpOn { get; private set; }
    public bool ValveOpen { get; private set; }
    public bool Irrigating { get; private set; }

    public StatePayload Current(string? commandId = null, string? result = null, string? reason = null) =>
        new(PumpOn, ValveOpen, Irrigating, commandId, result, reason);

    /// <summary>Simulated seconds the pump ran within [from, to).</summary>
    public double PumpSecondsBetween(TimeSpan from, TimeSpan to)
    {
        if (!PumpOn)
        {
            return 0;
        }

        var start = from > _pumpStartedAt ? from : _pumpStartedAt;
        var end = to < _pumpUntil ? to : _pumpUntil;
        return Math.Max(0, (end - start).TotalSeconds);
    }

    public IReadOnlyList<StatePayload> Handle(CommandPayload cmd, TelemetryPayload? latest, TimeSpan now)
    {
        StatePayload Reject(string reason) => Current(cmd.CommandId, CommandResults.Rejected, reason);
        StatePayload Done() => Current(cmd.CommandId, CommandResults.Completed);

        switch (cmd.Action)
        {
            case CommandActions.Irrigate:
                if (Irrigating || PumpOn) return [Reject(RejectReasons.Busy)];
                if (SafetyProblem(latest) is { } problem) return [Reject(problem)];
                if (cmd.DurationS is not > 0) return [Reject(RejectReasons.Invalid)];
                ValveOpen = true; // valve first, then pump: never pump against a closed valve
                StartPump(now, cmd.DurationS.Value, cmd.CommandId);
                Irrigating = true;
                return [Current(cmd.CommandId, CommandResults.Accepted)];

            case CommandActions.Stop:
                var interrupted = StopAll();
                return interrupted is null ? [Done()] : [Current(interrupted, CommandResults.Completed), Done()];

            case CommandActions.PumpOn:
                if (PumpOn) return [Reject(RejectReasons.Busy)];
                if (!ValveOpen) return [Reject(RejectReasons.ValveClosed)];
                if (SafetyProblem(latest) is { } pumpProblem) return [Reject(pumpProblem)];
                if (cmd.DurationS is not > 0) return [Reject(RejectReasons.Invalid)];
                StartPump(now, cmd.DurationS.Value, cmd.CommandId);
                return [Current(cmd.CommandId, CommandResults.Accepted)];

            case CommandActions.PumpOff:
                var pumpCommand = _activeCommandId;
                PumpOn = false;
                Irrigating = false;
                _activeCommandId = null;
                return pumpCommand is null ? [Done()] : [Current(pumpCommand, CommandResults.Completed), Done()];

            case CommandActions.ValveOpen:
                ValveOpen = true;
                return [Done()];

            case CommandActions.ValveClose:
                if (PumpOn) return [Reject(RejectReasons.PumpRunning)];
                ValveOpen = false;
                return [Done()];

            default:
                return [Reject(RejectReasons.Invalid)];
        }
    }

    /// <summary>Ends timed runs and enforces dry-run protection between commands.</summary>
    public IReadOnlyList<StatePayload> Tick(TelemetryPayload latest, TimeSpan now)
    {
        if (!PumpOn)
        {
            return [];
        }

        if (now >= _pumpUntil || latest.LevelPct < Config.LevelMinPct)
        {
            var finished = _activeCommandId;
            var tankEmpty = now < _pumpUntil;
            PumpOn = false;
            if (Irrigating)
            {
                ValveOpen = false;
                Irrigating = false;
            }

            _activeCommandId = null;
            return [Current(finished, CommandResults.Completed, tankEmpty ? RejectReasons.TankEmpty : null)];
        }

        return [];
    }

    private void StartPump(TimeSpan now, int durationS, string commandId)
    {
        PumpOn = true;
        _pumpStartedAt = now;
        _pumpUntil = now + TimeSpan.FromSeconds(Math.Min(durationS, Config.PumpMaxS));
        _activeCommandId = commandId;
    }

    /// <returns>The command that was running, if any.</returns>
    private string? StopAll()
    {
        var running = _activeCommandId;
        PumpOn = false;
        ValveOpen = false;
        Irrigating = false;
        _activeCommandId = null;
        return running;
    }

    private string? SafetyProblem(TelemetryPayload? latest)
    {
        if (latest is null) return RejectReasons.Invalid; // no sensor reading yet
        if (latest.LevelPct < Config.LevelMinPct) return RejectReasons.TankEmpty;
        if (latest.Ph < Config.PhMin || latest.Ph > Config.PhMax || latest.TurbidityNtu > Config.TurbidityMaxNtu)
        {
            return RejectReasons.WaterQuality;
        }

        return null;
    }
}
