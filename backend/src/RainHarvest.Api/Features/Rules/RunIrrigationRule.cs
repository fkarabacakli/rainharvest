using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Events;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Features.Commands;
using RainHarvest.Api.Features.Settings;
using RainHarvest.Api.Features.Telemetry;
using RainHarvest.Api.Infrastructure.Mqtt;
using RainHarvest.Api.Infrastructure.Persistence;
using RainHarvest.Contracts;

namespace RainHarvest.Api.Features.Rules;

/// <summary>
/// On every stored reading: gather the rule's inputs, decide, and send the
/// resulting command through the same path as a manual one (source = Rule).
/// Readings are processed one at a time, so two decisions for the same device
/// never race.
/// </summary>
public sealed partial class RunIrrigationRule(
    AppDbContext db,
    DeviceSettingsService settingsService,
    CommandSender sender,
    TimeProvider timeProvider,
    ILogger<RunIrrigationRule> logger) : IDomainEventHandler<TelemetryReceived>
{
    public async Task HandleAsync(TelemetryReceived e, CancellationToken cancellationToken)
    {
        var deviceId = e.Reading.DeviceId;
        var now = timeProvider.GetUtcNow();
        var settings = await settingsService.GetEffectiveAsync(deviceId, cancellationToken);
        if (!settings.AutoIrrigation)
        {
            return;
        }

        var irrigating = await db.Devices.Where(d => d.Id == deviceId).Select(d => d.Irrigating).SingleAsync(cancellationToken);
        var pendingSince = now - CommandTimeoutWorker.Timeout;
        var commandPending = await db.DeviceCommands.AnyAsync(
            c => c.DeviceId == deviceId && c.Status == CommandStatus.Sent && c.CreatedAt > pendingSince, cancellationToken);

        var lastAuto = await db.DeviceCommands
            .Where(c => c.DeviceId == deviceId && c.Source == CommandSource.Rule && c.Action == CommandActions.Irrigate)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.CreatedAt, c.Status })
            .FirstOrDefaultAsync(cancellationToken);

        var lastAccepted = await db.DeviceCommands
            .Where(c => c.DeviceId == deviceId && c.Action == CommandActions.Irrigate && c.Status == CommandStatus.Accepted)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => (CommandSource?)c.Source)
            .FirstOrDefaultAsync(cancellationToken);

        var decision = IrrigationRule.Decide(new IrrigationRule.Input(
            e.Reading,
            settings,
            irrigating,
            AutoIrrigationRunning: irrigating && lastAccepted == CommandSource.Rule,
            commandPending,
            lastAuto?.CreatedAt,
            now));

        var (action, durationS) = decision switch
        {
            IrrigationRule.Irrigate irrigate => (CommandActions.Irrigate, (int?)irrigate.DurationS),
            IrrigationRule.Stop => (CommandActions.Stop, null),
            _ => (null, null),
        };

        if (action is null)
        {
            LogNoAction(deviceId, decision.Reason);
            return;
        }

        try
        {
            await sender.SendAsync(deviceId, action, durationS, CommandSource.Rule, requestedBy: null, decision.Reason, cancellationToken);
            LogCommandSent(deviceId, action, decision.Reason);
        }
        catch (MqttNotConnectedException)
        {
            LogBrokerDown(deviceId, action);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Irrigation rule for {DeviceId}: no action ({Reason})")]
    private partial void LogNoAction(string deviceId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Irrigation rule sent '{Action}' to {DeviceId}: {Reason}")]
    private partial void LogCommandSent(string deviceId, string action, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Irrigation rule wanted '{Action}' for {DeviceId} but MQTT is disconnected")]
    private partial void LogBrokerDown(string deviceId, string action);
}
