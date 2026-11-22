using Microsoft.AspNetCore.Http.HttpResults;

namespace RainHarvest.Api.Common.Endpoints;

/// <summary>Shared ProblemDetails responses so every slice reports errors the same way.</summary>
public static class ApiProblems
{
    public static ProblemHttpResult DeviceNotFound(string deviceId) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Device not found",
            detail: $"No device with id '{deviceId}'.");

    public static ProblemHttpResult NoReadings(string deviceId) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "No readings",
            detail: $"Device '{deviceId}' has not sent any telemetry yet.");
}
