using RainHarvest.Api.Features.Devices;

namespace RainHarvest.Api.Features;

/// <summary>Services that feature slices share. Endpoints and MQTT handlers are discovered automatically.</summary>
public static class FeatureServices
{
    public static IServiceCollection AddFeatureServices(this IServiceCollection services)
    {
        services.AddScoped<DeviceTracker>();
        return services;
    }
}
