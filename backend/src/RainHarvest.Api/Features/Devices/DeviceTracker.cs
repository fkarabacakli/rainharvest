using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Devices;

/// <summary>
/// Keeps the <c>devices</c> table in step with what arrives over MQTT.
/// Unknown devices are registered automatically on first contact (name = id).
/// Each method is one idempotent upsert, so out-of-order or repeated
/// messages cannot create duplicates.
/// </summary>
public sealed class DeviceTracker(AppDbContext db)
{
    /// <summary>A message from the device proves it is online.</summary>
    public Task MarkSeenAsync(string deviceId, DateTimeOffset at, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO devices (id, name, is_online, last_seen_at, created_at)
            VALUES ({deviceId}, {deviceId}, TRUE, {at}, {at})
            ON CONFLICT (id) DO UPDATE
            SET is_online = TRUE,
                last_seen_at = GREATEST(devices.last_seen_at, EXCLUDED.last_seen_at)
            """,
            cancellationToken);

    /// <summary>The device said "offline" (or the broker published its Last Will).</summary>
    public Task MarkOfflineAsync(string deviceId, DateTimeOffset at, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO devices (id, name, is_online, last_seen_at, created_at)
            VALUES ({deviceId}, {deviceId}, FALSE, NULL, {at})
            ON CONFLICT (id) DO UPDATE SET is_online = FALSE
            """,
            cancellationToken);
}
