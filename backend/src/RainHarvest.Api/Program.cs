using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.OpenApi;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Common.Events;
using RainHarvest.Api.Common.Health;
using RainHarvest.Api.Features;
using RainHarvest.Api.Features.Live;
using RainHarvest.Api.Infrastructure.Auth;
using RainHarvest.Api.Infrastructure.Mqtt;
using RainHarvest.Api.Infrastructure.OpenApi;
using RainHarvest.Api.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi(o =>
{
    // Slices nest their own Request/Response types; prefix them with the slice
    // name (GetDevice.Response -> GetDeviceResponse) so schema ids never collide.
    o.CreateSchemaReferenceId = typeInfo =>
        typeInfo.Type is { IsNested: true, DeclaringType: { } slice, IsGenericType: false } type
            ? slice.Name + type.Name
            : OpenApiOptions.CreateDefaultSchemaReferenceId(typeInfo);

    o.AddDocumentTransformer<SecuritySchemeTransformer>();
    o.AddOperationTransformer<SecuritySchemeTransformer>();
});

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddMqtt(builder.Configuration, typeof(Program).Assembly);
builder.Services.AddEndpoints(typeof(Program).Assembly);
builder.Services.AddFeatureServices();
builder.Services.AddDomainEvents(typeof(Program).Assembly);
// The panel runs on its own origin. The SignalR JS client negotiates with
// withCredentials: true by default, which browsers only allow if the response
// says Allow-Credentials. Safe here: auth is a bearer token, never a cookie, so
// there is nothing for a cross-site request to ride on. Origins stay explicit.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database")
    .AddCheck<MqttHealthCheck>("mqtt");

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    // OAuth2 password flow preselected and prefilled with the seed admin, so
    // "Authorize" logs in without copy-pasting tokens. Development only.
    app.MapScalarApiReference(o => o
            .AddPreferredSecuritySchemes([SecuritySchemeTransformer.OAuth2Scheme])
            .AddPasswordFlow(SecuritySchemeTransformer.OAuth2Scheme, flow => flow
                .WithUsername(app.Configuration["Seed:AdminUsername"] ?? "")
                .WithPassword(app.Configuration["Seed:AdminPassword"] ?? ""))
            .EnablePersistentAuthentication())
        .AllowAnonymous();
}

await app.ApplyMigrationsIfEnabledAsync();
await app.SeedAdminIfConfiguredAsync();
await app.SeedDemoDataIfConfiguredAsync();

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync })
    .AllowAnonymous();

app.MapHub<LiveHub>(LiveHub.Path);

app.MapGroup("/api/v1")
    .AddEndpointFilterFactory(DeviceAccess.FilterFactory)
    .MapEndpoints();

await app.RunAsync();

/// <summary>Entry point; public so integration tests can use WebApplicationFactory.</summary>
public partial class Program;
