using RainHarvest.Api.Features.Auth;
using RainHarvest.Api.Features.Commands;
using RainHarvest.Api.Features.Devices;
using RainHarvest.Api.Features.Settings;

namespace RainHarvest.Api.Features;

/// <summary>Services that feature slices share. Endpoints and MQTT handlers are discovered automatically.</summary>
public static class FeatureServices
{
    public static IServiceCollection AddFeatureServices(this IServiceCollection services)
    {
        services.AddScoped<DeviceTracker>();
        services.AddScoped<CredentialVerifier>();
        services.AddScoped<DeviceSettingsService>();
        services.AddScoped<CommandSender>();
        services.AddHostedService<CommandTimeoutWorker>();
        return services;
    }
}
