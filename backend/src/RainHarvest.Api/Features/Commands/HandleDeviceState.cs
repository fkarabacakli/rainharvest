using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Mqtt;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Features.Devices;
using RainHarvest.Api.Infrastructure.Persistence;
using RainHarvest.Contracts;

namespace RainHarvest.Api.Features.Commands;

/// <summary>
/// MQTT <c>rainwater/{deviceId}/state</c> (retained): store the actuator state the
/// device reports and advance the status of the command it answers.
/// Status only moves forward, so a retained message replayed after a backend
/// restart cannot undo a later outcome.
/// </summary>
[MqttTopic(MqttTopics.Root + "/+/" + MqttTopics.State)]
public sealed partial class HandleDeviceState(
    AppDbContext db,
    DeviceTracker tracker,
    ILogger<HandleDeviceState> logger) : IMqttHandler
{
    public async Task HandleAsync(MqttMessage message, CancellationToken cancellationToken)
    {
        if (!MqttTopics.TryGetDeviceId(message.Topic, out var deviceId) || !Device.IsValidId(deviceId) || message.Payload.IsEmpty)
        {
            return;
        }

        StatePayload? state;
        try
        {
            state = JsonSerializer.Deserialize<StatePayload>(message.Payload.Span, MqttJson.Options);
        }
        catch (JsonException ex)
        {
            LogMalformed(deviceId, ex.Message);
            return;
        }

        if (state is null)
        {
            return;
        }

        await tracker.MarkSeenAsync(deviceId, message.ReceivedAt, cancellationToken);
        await db.Devices.Where(d => d.Id == deviceId).ExecuteUpdateAsync(s => s
            .SetProperty(d => d.PumpOn, state.PumpOn)
            .SetProperty(d => d.ValveOpen, state.ValveOpen)
            .SetProperty(d => d.Irrigating, state.Irrigating)
            .SetProperty(d => d.StateReportedAt, message.ReceivedAt), cancellationToken);

        if (state.LastCommandId is { } rawId && Guid.TryParse(rawId, out var commandId) && ToStatus(state.LastCommandResult) is { } next)
        {
            await AdvanceCommandAsync(deviceId, commandId, next, state.Reason, message.ReceivedAt, cancellationToken);
        }
    }

    private async Task AdvanceCommandAsync(
        string deviceId, Guid commandId, CommandStatus next, string? reason, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var command = await db.DeviceCommands.SingleOrDefaultAsync(c => c.Id == commandId && c.DeviceId == deviceId, cancellationToken);
        if (command is null)
        {
            LogUnknownCommand(deviceId, commandId);
            return;
        }

        if (!CanMove(command.Status, next))
        {
            return;
        }

        command.Status = next;
        command.StatusReason = next == CommandStatus.Rejected ? reason ?? RejectReasons.Invalid : null;
        command.UpdatedAt = at;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Forward-only transitions. A late answer may still replace "Expired",
    /// because the device's word beats our timeout guess.
    /// </summary>
    public static bool CanMove(CommandStatus current, CommandStatus next) => (current, next) switch
    {
        (CommandStatus.Sent or CommandStatus.Expired, CommandStatus.Accepted or CommandStatus.Rejected or CommandStatus.Completed) => true,
        (CommandStatus.Accepted, CommandStatus.Completed) => true,
        _ => false,
    };

    private static CommandStatus? ToStatus(string? result) => result switch
    {
        CommandResults.Accepted => CommandStatus.Accepted,
        CommandResults.Rejected => CommandStatus.Rejected,
        CommandResults.Completed => CommandStatus.Completed,
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring malformed state from {DeviceId}: {Reason}")]
    private partial void LogMalformed(string deviceId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Device {DeviceId} reported unknown command {CommandId}")]
    private partial void LogUnknownCommand(string deviceId, Guid commandId);
}
