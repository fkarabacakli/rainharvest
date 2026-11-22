using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RainHarvest.Api.Features.Auth;
using RainHarvest.Api.IntegrationTests.Infrastructure;
using static RainHarvest.Api.IntegrationTests.Infrastructure.TestHelpers;

namespace RainHarvest.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("/api/v1/devices")]
    [InlineData("/api/v1/sites")]
    [InlineData("/api/v1/auth/me")]
    [InlineData("/api/v1/does-not-exist")]
    public async Task Requests_without_a_token_get_401_problem_details(string url)
    {
        using var client = fixture.CreateAnonymousClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_tampered_or_foreign_token_is_rejected()
    {
        using var admin = await fixture.CreateAdminClientAsync();
        var token = admin.DefaultRequestHeaders.Authorization!.Parameter!;
        using var client = fixture.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^4] + "AAAA");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/devices")).StatusCode);
    }

    [Theory]
    [InlineData(ApiFixture.AdminUsername, "wrong-password")]
    [InlineData("nobody-here", "whatever-password")]
    public async Task Wrong_credentials_get_the_same_401(string username, string password)
    {
        using var client = fixture.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid credentials", (await response.Content.ReadFromJsonAsync<ProblemBody>(Json))!.Title);
    }

    [Fact]
    public async Task Login_is_case_insensitive_and_me_returns_the_user()
    {
        using var client = await fixture.CreateClientAsync(ApiFixture.AdminUsername.ToUpperInvariant(), ApiFixture.AdminPassword);

        var me = await client.GetFromJsonAsync<GetCurrentUser.Response>("/api/v1/auth/me", Json);

        Assert.Equal(ApiFixture.AdminUsername, me!.Username);
        Assert.Equal(Domain.UserRole.Admin, me.Role);
    }

    [Theory]
    [InlineData("POST", "/api/v1/sites")]
    [InlineData("POST", "/api/v1/users")]
    [InlineData("GET", "/api/v1/users")]
    [InlineData("POST", "/api/v1/devices")]
    public async Task Regular_users_get_403_on_admin_endpoints(string method, string url)
    {
        var (client, _) = await fixture.CreateUserClientAsync();
        using var _client = client;

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = method == "GET" ? null : JsonContent.Create(new { name = "x", id = "x", username = "xyz", password = "12345678" }),
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Creating_users_validates_input_and_rejects_duplicates()
    {
        using var admin = await fixture.CreateAdminClientAsync();
        var username = $"dup-{Guid.NewGuid():N}"[..16];

        var weak = await admin.PostAsJsonAsync("/api/v1/users", new { username = "a b", password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var errors = (await weak.Content.ReadFromJsonAsync<ProblemBody>(Json))!.Errors!;
        Assert.Contains("username", errors.Keys);
        Assert.Contains("password", errors.Keys);

        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync("/api/v1/users", new { username, password = "long-enough-1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/v1/users", new { username = username.ToUpperInvariant(), password = "long-enough-1" })).StatusCode);
    }

    private sealed record ProblemBody(string? Title, Dictionary<string, string[]>? Errors);
}
