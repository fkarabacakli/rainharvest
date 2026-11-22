namespace RainHarvest.Api.Common.Endpoints;

/// <summary>A validated <c>[from, to)</c> query window.</summary>
public readonly record struct TimeRange(DateTimeOffset From, DateTimeOffset To)
{
    /// <summary>
    /// Fills in defaults (<c>to</c> = now, <c>from</c> = <c>to</c> − <paramref name="defaultSpan"/>)
    /// and checks order and maximum span. Returns validation errors keyed by query parameter.
    /// </summary>
    public static bool TryResolve(
        DateTimeOffset? from,
        DateTimeOffset? to,
        DateTimeOffset now,
        TimeSpan defaultSpan,
        TimeSpan maxSpan,
        out TimeRange range,
        out Dictionary<string, string[]> errors)
    {
        var end = (to ?? now).ToUniversalTime();
        var start = (from ?? end - defaultSpan).ToUniversalTime();
        range = new TimeRange(start, end);
        errors = [];

        if (start >= end)
        {
            errors["from"] = ["'from' must be earlier than 'to'."];
        }
        else if (end - start > maxSpan)
        {
            errors["from"] = [$"The window may span at most {Describe(maxSpan)}."];
        }

        return errors.Count == 0;
    }

    private static string Describe(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{span.TotalDays:0.##} days" : $"{span.TotalHours:0.##} hours";
}
