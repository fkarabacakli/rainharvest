using System.Reflection;
using RainHarvest.Api.Common.Mqtt;

namespace RainHarvest.Api.Infrastructure.Mqtt;

public static class MqttServiceCollectionExtensions
{
    public static IServiceCollection AddMqtt(this IServiceCollection services, IConfiguration configuration, Assembly handlerAssembly)
    {
        services.AddOptions<MqttOptions>()
            .Bind(configuration.GetSection(MqttOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var registry = MqttHandlerRegistry.FromAssembly(handlerAssembly);
        services.AddSingleton(registry);
        foreach (var handlerType in registry.Registrations.Select(r => r.HandlerType).Distinct())
        {
            services.AddScoped(handlerType);
        }

        services.AddSingleton<MqttConnection>();
        services.AddSingleton<MqttDispatcher>();
        services.AddSingleton<IMqttPublisher, MqttPublisher>();
        services.AddHostedService<MqttWorker>();

        return services;
    }
}
