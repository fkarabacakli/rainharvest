using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Infrastructure.Auth;

/// <summary>
/// Creates the first admin from <c>Seed:AdminUsername</c> / <c>Seed:AdminPassword</c>
/// when no admin exists yet. Does nothing once any admin exists, so changing the
/// settings later never resets a password.
/// </summary>
public static partial class AdminSeeder
{
    public static async Task SeedAdminIfConfiguredAsync(this WebApplication app)
    {
        var username = app.Configuration["Seed:AdminUsername"];
        var password = app.Configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Users.AnyAsync(u => u.Role == UserRole.Admin))
        {
            return;
        }

        var normalized = User.Normalize(username);
        if (!User.IsValidUsername(normalized) || password.Length < User.MinPasswordLength)
        {
            throw new InvalidOperationException(
                $"Seed admin is invalid: username must match [a-z0-9._-]{{3,64}}, password at least {User.MinPasswordLength} characters.");
        }

        var admin = new User
        {
            Id = Guid.CreateVersion7(),
            Username = normalized,
            PasswordHash = "",
            Role = UserRole.Admin,
            CreatedAt = TimeProvider.System.GetUtcNow(),
        };
        admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, password);

        db.Users.Add(admin);
        await db.SaveChangesAsync();
        LogSeeded(app.Logger, normalized);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded admin user '{Username}'")]
    private static partial void LogSeeded(ILogger logger, string username);
}
