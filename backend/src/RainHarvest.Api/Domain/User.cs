using System.Text.RegularExpressions;

namespace RainHarvest.Api.Domain;

public enum UserRole
{
    /// <summary>Sees and operates the devices of the sites they are a member of.</summary>
    User,

    /// <summary>Sees everything; manages users, sites, memberships and device assignment.</summary>
    Admin,
}

public sealed partial class User
{
    public const int MinPasswordLength = 8;

    public Guid Id { get; init; }

    /// <summary>Stored lower-case; login is case-insensitive.</summary>
    public required string Username { get; init; }

    public required string PasswordHash { get; set; }

    public UserRole Role { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public static string Normalize(string username) => username.Trim().ToLowerInvariant();

    /// <summary>3–64 chars: lower-case letters, digits, '.', '_' and '-'. Pass a normalized name.</summary>
    public static bool IsValidUsername(string normalized) => UsernamePattern().IsMatch(normalized);

    [GeneratedRegex("^[a-z0-9._-]{3,64}$")]
    private static partial Regex UsernamePattern();
}
