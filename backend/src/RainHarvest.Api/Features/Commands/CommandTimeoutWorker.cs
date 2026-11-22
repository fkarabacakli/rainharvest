using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Common.Events;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Commands;

/// <summary>Marks commands the device never answered as Expired.</summary>
public sealed partial class CommandTimeoutWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<CommandTimeoutWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = timeProvider.GetUtcNow();
                var cutoff = now - Timeout;

                var stale = await db.DeviceCommands
                    .Where(c => c.Status == CommandStatus.Sent && c.CreatedAt < cutoff)
                    .ToListAsync(stoppingToken);
                if (stale.Count == 0)
                {
                    continue;
                }

                foreach (var command in stale)
                {
                    command.Status = CommandStatus.Expired;
                    command.StatusReason = "timeout";
                    command.UpdatedAt = now;
                }

                await db.SaveChangesAsync(stoppingToken);
                LogExpired(stale.Count);

                var events = scope.ServiceProvider.GetRequiredService<DomainEventPublisher>();
                foreach (var command in stale)
                {
                    await events.PublishAsync(new CommandUpdated(CommandDto.From(command)), stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} command(s) expired without an answer")]
    private partial void LogExpired(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Command timeout sweep failed")]
    private partial void LogFailed(Exception exception);
}
