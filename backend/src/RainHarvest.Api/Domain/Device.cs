using System.Text.RegularExpressions;

namespace RainHarvest.Api.Domain;

public sealed partial class Device
{
    public const int MaxIdLength = 64;

    /// <summary>Letters, digits, '-' and '_' only, so ids are safe in MQTT topics and URLs.</summary>
    public static bool IsValidId(string id) => id.Length <= MaxIdLength && IdPattern().IsMatch(id);

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex IdPattern();

    /// <summary>The id the device uses in its MQTT topics, e.g. <c>node1</c>.</summary>
    public required string Id { get; init; }

    public required string Name { get; set; }

    /// <summary>The site (mahal) the device belongs to; null = unassigned, visible to admins only.</summary>
    public Guid? SiteId { get; set; }

    public bool IsOnline { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}
