using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using RainHarvest.Api.IntegrationTests.Infrastructure;

namespace RainHarvest.Api.IntegrationTests;

/// <summary>
/// The panel generates its client from this document, so its shape is part of
/// the contract. OpenAPI is only mapped in Development, hence the override.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OpenApiDocumentTests(ApiFixture fixture)
{
    [Fact]
    public async Task Every_endpoint_gets_its_own_response_and_request_schemas()
    {
        var doc = await GetDocumentAsync();
        var schemas = doc.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Select(p => p.Name).ToList();

        // Bare nested names mean two slices' types were merged into one schema.
        Assert.DoesNotContain("Response", schemas);
        Assert.DoesNotContain("Request", schemas);
        Assert.Contains("GetDeviceResponse", schemas);
        Assert.Contains("TokenResponse", schemas);
        Assert.Contains("GetReadingAggregatesResponse", schemas);

        Assert.Equal("#/components/schemas/GetCurrentUserResponse", ResponseRef(doc, "/api/v1/auth/me", "get"));
        Assert.Equal("#/components/schemas/TokenResponse", ResponseRef(doc, "/api/v1/auth/token", "post"));
    }

    [Fact]
    public async Task Token_endpoint_documents_the_password_grant_form()
    {
        var doc = await GetDocumentAsync();
        var schema = doc.GetProperty("paths").GetProperty("/api/v1/auth/token").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/x-www-form-urlencoded").GetProperty("schema");

        Assert.Equal(["grant_type", "password", "username"],
            schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).Order());
        var grantType = schema.GetProperty("properties").GetProperty("grant_type");
        Assert.Equal(["password"], grantType.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));

        var oauth2 = doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("OAuth2");
        Assert.Equal("/api/v1/auth/token", oauth2.GetProperty("flows").GetProperty("password").GetProperty("tokenUrl").GetString());
    }

    private async Task<JsonElement> GetDocumentAsync()
    {
        await using var devFactory = fixture.Factory.WithWebHostBuilder(b => b.UseEnvironment("Development"));
        using var client = devFactory.CreateClient();
        return await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
    }

    private static string? ResponseRef(JsonElement doc, string path, string method) =>
        doc.GetProperty("paths").GetProperty(path).GetProperty(method)
            .GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
}
