using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using RainHarvest.Api.Common.Auth;
using RainHarvest.Api.Common.Endpoints;
using RainHarvest.Api.Common.Health;
using RainHarvest.Api.Features;
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
    o.AddDocumentTransformer<BearerSecurityTransformer>();
    o.AddOperationTransformer<BearerSecurityTransformer>();
});

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddMqtt(builder.Configuration, typeof(Program).Assembly);
builder.Services.AddEndpoints(typeof(Program).Assembly);
builder.Services.AddFeatureServices();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database")
    .AddCheck<MqttHealthCheck>("mqtt");

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

await app.ApplyMigrationsIfEnabledAsync();
await app.SeedAdminIfConfiguredAsync();

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync })
    .AllowAnonymous();

app.MapGroup("/api/v1")
    .AddEndpointFilterFactory(DeviceAccess.FilterFactory)
    .MapEndpoints();

await app.RunAsync();

/// <summary>Entry point; public so integration tests can use WebApplicationFactory.</summary>
public partial class Program;
