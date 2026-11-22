using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Infrastructure.Auth;

/// <summary>
/// Development convenience (<c>Seed:DemoData = true</c>): a site "Demo Ev", a
/// regular user who belongs to it, and the simulator's devices pre-registered
/// in it. Lets the panel developer see both the admin and the member view on
/// first start. Runs once: skipped when the demo user already exists.
/// </summary>
public static partial class DemoDataSeeder
{
    public const string SiteName = "Demo Ev";

    public static async Task SeedDemoDataIfConfiguredAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Seed:DemoData"))
        {
            return;
        }

        var username = User.Normalize(app.Configuration["Seed:DemoUsername"] ?? "demo");
        var password = app.Configuration["Seed:DemoPassword"] ?? "";
        var deviceIds = app.Configuration.GetSection("Seed:DemoDevices").Get<string[]>() ?? [];
        if (password.Length < User.MinPasswordLength)
        {
            throw new InvalidOperationException($"Seed:DemoPassword must be at least {User.MinPasswordLength} characters.");
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Users.AnyAsync(u => u.Username == username))
        {
            return;
        }

        var now = TimeProvider.System.GetUtcNow();
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Name == SiteName)
                   ?? db.Sites.Add(new Site { Id = Guid.CreateVersion7(), Name = SiteName, CreatedAt = now }).Entity;

        var user = new User { Id = Guid.CreateVersion7(), Username = username, PasswordHash = "", Role = UserRole.User, CreatedAt = now };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        db.Users.Add(user);
        db.SiteMembers.Add(new SiteMember { SiteId = site.Id, UserId = user.Id, AddedAt = now });

        foreach (var id in deviceIds.Where(Device.IsValidId))
        {
            var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == id);
            if (device is null)
            {
                db.Devices.Add(new Device { Id = id, Name = $"Demo {id}", SiteId = site.Id, CreatedAt = now });
            }
            else
            {
                device.SiteId ??= site.Id; // an already connected device that nobody assigned yet
            }
        }

        await db.SaveChangesAsync();
        LogSeeded(app.Logger, username, SiteName, string.Join(", ", deviceIds));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded demo user '{Username}' in site '{Site}' with devices {Devices}")]
    private static partial void LogSeeded(ILogger logger, string username, string site, string devices);
}
