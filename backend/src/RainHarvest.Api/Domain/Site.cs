namespace RainHarvest.Api.Domain;

/// <summary>A place (mahal) such as a home or an office. Users see the devices of the sites they belong to.</summary>
public sealed class Site
{
    public const int MaxNameLength = 128;

    public Guid Id { get; init; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Membership of a user in a site. No per-site role yet: every member can see
/// and operate the site's devices. A role column can be added later (PLAN.md §5).
/// </summary>
public sealed class SiteMember
{
    public Guid SiteId { get; init; }

    public Guid UserId { get; init; }

    public DateTimeOffset AddedAt { get; init; }
}
