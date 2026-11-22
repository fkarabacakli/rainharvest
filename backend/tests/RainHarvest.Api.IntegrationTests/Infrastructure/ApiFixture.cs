using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RainHarvest.Api.Features.Auth;
using RainHarvest.Api.Features.Users;
using Testcontainers.PostgreSql;

namespace RainHarvest.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Starts real TimescaleDB and Mosquitto containers once per test collection
/// and runs the API against them. Requires a running Docker daemon.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    // Keep in sync with docker-compose.yml.
    public const string TimescaleImage = "timescale/timescaledb:2.32.1-pg17";
    public const string MosquittoImage = "eclipse-mosquitto:2.0.23";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder(TimescaleImage)
        .WithDatabase("rainharvest")
        .Build();

    private readonly IContainer _broker = new ContainerBuilder(MosquittoImage)
        .WithPortBinding(1883, assignRandomHostPort: true)
        .WithResourceMapping(
            Encoding.UTF8.GetBytes("listener 1883\nallow_anonymous true\n"),
            FilePath.Of("/mosquitto/config/mosquitto.conf"))
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("mosquitto version .* running"))
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public const string AdminUsername = "admin";
    public const string AdminPassword = "admin-test-password";

    public string ConnectionString => _db.GetConnectionString();

    public string BrokerHost => _broker.Hostname;

    public int BrokerPort => _broker.GetMappedPublicPort(1883);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_db.StartAsync(), _broker.StartAsync());

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = ConnectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["Mqtt:Host"] = _broker.Hostname,
            ["Mqtt:Port"] = _broker.GetMappedPublicPort(1883).ToString(),
            ["Mqtt:Username"] = "",
            ["Mqtt:Password"] = "",
            ["Jwt:SigningKey"] = "integration-test-signing-key-0123456789abcdef",
            ["Seed:AdminUsername"] = AdminUsername,
            ["Seed:AdminPassword"] = AdminPassword,
        };

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        });

        // Start the host (runs migrations) and wait until the MQTT worker has
        // subscribed; messages published before that would be lost.
        using var client = Factory.CreateClient();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((await client.GetAsync("/health")).StatusCode != System.Net.HttpStatusCode.OK)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("API did not become healthy against the test containers.");
            }

            await Task.Delay(250);
        }
    }

    /// <summary>A client without a token.</summary>
    public HttpClient CreateAnonymousClient() => Factory.CreateClient();

    /// <summary>A client logged in as the seeded admin.</summary>
    public Task<HttpClient> CreateAdminClientAsync() => CreateClientAsync(AdminUsername, AdminPassword);

    /// <summary>Creates a fresh regular user (unique name) and returns a client logged in as them.</summary>
    public async Task<(HttpClient Client, UserDto User)> CreateUserClientAsync(string prefix = "user")
    {
        using var admin = await CreateAdminClientAsync();
        var username = $"{prefix}-{Guid.NewGuid():N}"[..20];
        var created = await admin.PostAsJsonAsync("/api/v1/users", new { username, password = "user-test-password" });
        created.EnsureSuccessStatusCode();
        var user = (await created.Content.ReadFromJsonAsync<UserDto>(TestHelpers.Json))!;
        return (await CreateClientAsync(username, "user-test-password"), user);
    }

    public async Task<HttpClient> CreateClientAsync(string username, string password)
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsync("/api/v1/auth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password,
        }));
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<Token.Response>(TestHelpers.Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Task.WhenAll(_db.DisposeAsync().AsTask(), _broker.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
