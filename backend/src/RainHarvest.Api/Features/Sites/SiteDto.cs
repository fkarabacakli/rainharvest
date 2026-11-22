using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Features.Sites;

public sealed record SiteDto(Guid Id, string Name, DateTimeOffset CreatedAt)
{
    public static SiteDto From(Site s) => new(s.Id, s.Name, s.CreatedAt);

    /// <summary>Trims the name and checks its length; returns validation errors keyed by "name".</summary>
    public static bool TryNormalizeName(string? raw, out string name, out Dictionary<string, string[]> errors)
    {
        name = (raw ?? "").Trim();
        errors = [];
        if (name.Length is 0 or > Site.MaxNameLength)
        {
            errors["name"] = [$"1–{Site.MaxNameLength} characters."];
        }

        return errors.Count == 0;
    }
}
