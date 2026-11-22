using Microsoft.AspNetCore.Http.HttpResults;

namespace RainHarvest.Api.Features.Sites;

internal static class SiteProblems
{
    public static ProblemHttpResult NotFound(Guid siteId) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Site not found",
            detail: $"No site with id '{siteId}'.");

    public static ProblemHttpResult UserNotFound(Guid userId) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "User not found",
            detail: $"No user with id '{userId}'.");
}
