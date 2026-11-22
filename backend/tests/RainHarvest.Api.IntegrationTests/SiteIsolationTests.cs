using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RainHarvest.Api.Features.Auth;
using RainHarvest.Api.Features.Devices;
using RainHarvest.Api.Features.Sites;
using RainHarvest.Api.Features.Telemetry;
using RainHarvest.Api.IntegrationTests.Infrastructure;
using static RainHarvest.Api.IntegrationTests.Infrastructure.TestHelpers;

namespace RainHarvest.Api.IntegrationTests;

/// <summary>
/// M2 acceptance: two users with their own homes; each sees only their own
/// devices, unassigned devices are admin-only, pre-registered devices land in
/// their site on first contact.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SiteIsolationTests(ApiFixture fixture) : IAsyncLifetime
{
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;
    private Guid _aliceId;
    private SiteDto _aliceHome = null!;
    private SiteDto _bobHome = null!;
    private readonly string _aliceDevice = NewDeviceId();
    private readonly string _bobDevice = NewDeviceId();
    private readonly string _strayDevice = NewDeviceId();

    public async Task InitializeAsync()
    {
        _admin = await fixture.CreateAdminClientAsync();
        (_alice, var alice) = await fixture.CreateUserClientAsync("alice");
        (_bob, var bob) = await fixture.CreateUserClientAsync("bob");
        _aliceId = alice.Id;

        _aliceHome = await CreateSiteAsync("Alice home");
        _bobHome = await CreateSiteAsync("Bob home");
        await AddMemberAsync(_aliceHome.Id, alice.Id);
        await AddMemberAsync(_bobHome.Id, bob.Id);

        // Pre-register (plug and play) before the devices ever connect.
        await RegisterAsync(_aliceDevice, "Alice tank", _aliceHome.Id);
        await RegisterAsync(_bobDevice, "Bob tank", _bobHome.Id);

        await using var mqtt = new TestDevice(fixture);
        await mqtt.ConnectAsync();
        foreach (var id in new[] { _aliceDevice, _bobDevice, _strayDevice })
        {
            await mqtt.PublishTelemetryAsync(id, SampleTelemetry());
        }

        foreach (var id in new[] { _aliceDevice, _bobDevice, _strayDevice })
        {
            await EventuallyAsync(() => TryGetAsync<ReadingDto>(_admin, $"/api/v1/devices/{id}/readings/latest"));
        }
    }

    public Task DisposeAsync()
    {
        _admin.Dispose();
        _alice.Dispose();
        _bob.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Each_user_lists_only_their_own_devices_and_sites()
    {
        var aliceDevices = await _alice.GetFromJsonAsync<List<DeviceDto>>("/api/v1/devices", Json);
        var bobDevices = await _bob.GetFromJsonAsync<List<DeviceDto>>("/api/v1/devices", Json);

        Assert.Equal([_aliceDevice], aliceDevices!.Select(d => d.Id));
        Assert.Equal([_bobDevice], bobDevices!.Select(d => d.Id));

        var aliceSites = await _alice.GetFromJsonAsync<List<SiteDto>>("/api/v1/sites", Json);
        Assert.Equal([_aliceHome.Id], aliceSites!.Select(s => s.Id));

        var me = await _alice.GetFromJsonAsync<GetCurrentUser.Response>("/api/v1/auth/me", Json);
        Assert.Equal([_aliceHome.Id], me!.Sites.Select(s => s.Id));
    }

    [Fact]
    public async Task Pre_registered_device_keeps_its_name_and_site_after_first_contact()
    {
        var detail = await _alice.GetFromJsonAsync<GetDevice.Response>($"/api/v1/devices/{_aliceDevice}", Json);

        Assert.Equal("Alice tank", detail!.Device.Name);
        Assert.Equal(_aliceHome.Id, detail.Device.SiteId);
        Assert.True(detail.Device.IsOnline);
        Assert.NotNull(detail.LatestReading);

        var site = await _alice.GetFromJsonAsync<GetSite.Response>($"/api/v1/sites/{_aliceHome.Id}", Json);
        Assert.Equal([_aliceDevice], site!.Devices.Select(d => d.Id));
    }

    [Fact]
    public async Task Other_peoples_and_unassigned_devices_look_like_they_do_not_exist()
    {
        foreach (var id in new[] { _bobDevice, _strayDevice })
        {
            var response = await _alice.GetAsync($"/api/v1/devices/{id}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Device not found", (await response.Content.ReadFromJsonAsync<ProblemBody>(Json))!.Title);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _alice.GetAsync($"/api/v1/sites/{_bobHome.Id}")).StatusCode);
    }

    [Fact]
    public async Task Admin_sees_unassigned_devices_and_can_assign_them()
    {
        var unassigned = await _admin.GetFromJsonAsync<List<DeviceDto>>("/api/v1/devices?unassigned=true", Json);
        Assert.Contains(unassigned!, d => d.Id == _strayDevice);

        var assign = await _admin.PutAsJsonAsync($"/api/v1/devices/{_strayDevice}/site", new { siteId = _bobHome.Id });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        var bobDevices = await _bob.GetFromJsonAsync<List<DeviceDto>>("/api/v1/devices", Json);
        Assert.Contains(bobDevices!, d => d.Id == _strayDevice);
        Assert.Equal(HttpStatusCode.OK, (await _bob.GetAsync($"/api/v1/devices/{_strayDevice}/readings/latest")).StatusCode);
    }

    [Fact]
    public async Task Removing_a_membership_takes_effect_with_the_same_token()
    {
        Assert.Equal(HttpStatusCode.OK, (await _alice.GetAsync($"/api/v1/devices/{_aliceDevice}")).StatusCode);

        var removed = await _admin.DeleteAsync($"/api/v1/sites/{_aliceHome.Id}/members/{_aliceId}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _alice.GetAsync($"/api/v1/devices/{_aliceDevice}")).StatusCode);
        Assert.Empty((await _alice.GetFromJsonAsync<List<DeviceDto>>("/api/v1/devices", Json))!);
    }

    [Fact]
    public async Task Admin_can_rename_a_self_registered_device_and_members_see_the_new_name()
    {
        await _admin.PutAsJsonAsync($"/api/v1/devices/{_strayDevice}/site", new { siteId = _aliceHome.Id });

        var invalid = await _admin.PutAsJsonAsync($"/api/v1/devices/{_strayDevice}/name", new { name = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var renamed = await _admin.PutAsJsonAsync($"/api/v1/devices/{_strayDevice}/name", new { name = "  Bahçe deposu " });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var seen = await _alice.GetFromJsonAsync<GetDevice.Response>($"/api/v1/devices/{_strayDevice}", Json);
        Assert.Equal("Bahçe deposu", seen!.Device.Name);

        // A member may see the device but not rename it.
        var byMember = await _alice.PutAsJsonAsync($"/api/v1/devices/{_strayDevice}/name", new { name = "mine" });
        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
    }

    [Fact]
    public async Task Registering_an_existing_device_conflicts()
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/devices", new { id = _strayDevice });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// Safety net for future slices: every route with {deviceId}, for every
    /// HTTP method, must refuse an outsider with 404 (or 403 if admin-only).
    /// </summary>
    [Fact]
    public async Task Every_device_route_refuses_outsiders()
    {
        var routes = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("{deviceId}", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (Method: method, Pattern: e.RoutePattern.RawText!)))
            .ToList();

        // 11 as of M3: detail, readings x3, site, name, settings x2, commands x3. Grows with later slices.
        Assert.True(routes.Count >= 11, $"Expected the device routes to be discovered, found {routes.Count}.");

        foreach (var (method, pattern) in routes)
        {
            var hasOtherParameters = System.Text.RegularExpressions.Regex.IsMatch(pattern.Replace("{deviceId}", ""), @"\{[^}]+\}");
            var url = "/" + System.Text.RegularExpressions.Regex.Replace(
                pattern.TrimStart('/').Replace("{deviceId}", _bobDevice, StringComparison.Ordinal),
                @"\{[^}]+\}",
                Guid.Empty.ToString()); // other route values (e.g. {commandId:guid}) get a placeholder
            var request = new HttpRequestMessage(new HttpMethod(method), url)
            {
                Content = method == "GET" ? null : JsonContent.Create(new { }),
            };

            var status = (await _alice.SendAsync(request)).StatusCode;
            Assert.True(status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
                $"{method} {pattern} returned {(int)status} to an outsider.");

            if (method == "GET" && !hasOtherParameters)
            {
                var ownerStatus = (await _bob.GetAsync(url)).StatusCode;
                Assert.True(ownerStatus == HttpStatusCode.OK, $"GET {pattern} returned {(int)ownerStatus} to the owner.");
            }
        }
    }

    private async Task<SiteDto> CreateSiteAsync(string name)
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/sites", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SiteDto>(Json))!;
    }

    private async Task AddMemberAsync(Guid siteId, Guid userId) =>
        (await _admin.PutAsync($"/api/v1/sites/{siteId}/members/{userId}", null)).EnsureSuccessStatusCode();

    private async Task RegisterAsync(string id, string name, Guid siteId) =>
        (await _admin.PostAsJsonAsync("/api/v1/devices", new { id, name, siteId })).EnsureSuccessStatusCode();

    private static async Task<T?> TryGetAsync<T>(HttpClient client, string url) where T : class
    {
        var response = await client.GetAsync(url);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>(Json) : null;
    }

    private sealed record ProblemBody(string? Title);
}
