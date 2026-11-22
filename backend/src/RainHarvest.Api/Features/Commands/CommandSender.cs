using RainHarvest.Api.Common.Events;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Mqtt;
using RainHarvest.Api.Infrastructure.Persistence;
using RainHarvest.Contracts;

namespace RainHarvest.Api.Features.Commands;

/// <summary>
/// Records a command and publishes it to the device. Used by the manual
/// endpoint and the irrigation rule, so both leave the same audit trail.
/// The outcome arrives later through the device's MQTT <c>state</c>.
/// </summary>
public sealed class CommandSender(AppDbContext db, IMqttPublisher publisher, DomainEventPublisher events, TimeProvider timeProvider)
{
    /// <exception cref="MqttNotConnectedException">Broker unreachable; nothing was recorded.</exception>
    public async Task<DeviceCommand> SendAsync(
        string deviceId,
        string action,
        int? durationS,
        CommandSource source,
        Guid? requestedBy,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (!publisher.IsConnected)
        {
            throw new MqttNotConnectedException();
        }

        var now = timeProvider.GetUtcNow();
        var command = new DeviceCommand
        {
            Id = Guid.CreateVersion7(),
            DeviceId = deviceId,
            Action = action,
            DurationS = CommandActions.NeedsDuration(action) ? durationS : null,
            Source = source,
            RequestedBy = requestedBy,
            Reason = reason,
            Status = CommandStatus.Sent,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Save first: the device may answer before PublishAsync returns, and its
        // state message must find the command.
        db.DeviceCommands.Add(command);
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new CommandUpdated(CommandDto.From(command)), cancellationToken);

        try
        {
            await publisher.PublishJsonAsync(
                MqttTopics.For(deviceId, MqttTopics.Command),
                new CommandPayload(command.Id.ToString(), action, command.DurationS),
                retain: false,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            command.Status = CommandStatus.Expired;
            command.StatusReason = "publish_failed";
            command.UpdatedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            await events.PublishAsync(new CommandUpdated(CommandDto.From(command)), CancellationToken.None);
            throw;
        }

        return command;
    }
}
