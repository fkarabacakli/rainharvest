using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Domain;
using RainHarvest.Api.Infrastructure.Persistence;

namespace RainHarvest.Api.Features.Auth;

/// <summary>
/// Username + password check used by <see cref="Token"/>. Kept separate so a
/// future login flow (or a password-change endpoint) reuses the same timing-safe check.
/// </summary>
public sealed class CredentialVerifier(AppDbContext db)
{
    // Spends the same hashing time for unknown usernames, so response time
    // does not reveal which usernames exist.
    private static readonly User TimingDummy = new() { Username = "-", PasswordHash = "" };
    private static readonly string TimingDummyHash = new PasswordHasher<User>().HashPassword(TimingDummy, "timing-dummy");

    /// <returns>The user, or null when the username is unknown or the password is wrong.</returns>
    public async Task<User?> VerifyAsync(string? username, string? password, CancellationToken cancellationToken)
    {
        var hasher = new PasswordHasher<User>();
        var normalized = User.Normalize(username ?? "");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == normalized, cancellationToken);

        if (user is null)
        {
            hasher.VerifyHashedPassword(TimingDummy, TimingDummyHash, password ?? "");
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "");
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password!);
            await db.SaveChangesAsync(cancellationToken);
        }

        return user;
    }
}
