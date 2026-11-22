using System.Reflection;

namespace RainHarvest.Api.Common.Mqtt;

/// <summary>Maps topic filters to handler types. Built once at startup.</summary>
public sealed class MqttHandlerRegistry(IReadOnlyList<MqttHandlerRegistration> registrations)
{
    public IReadOnlyList<MqttHandlerRegistration> Registrations { get; } = registrations;

    public IReadOnlyCollection<string> Filters { get; } =
        registrations.Select(r => r.Filter).Distinct(StringComparer.Ordinal).ToArray();

    public IEnumerable<Type> HandlersFor(string topic) =>
        Registrations.Where(r => TopicMatcher.Matches(r.Filter, topic)).Select(r => r.HandlerType).Distinct();

    public static MqttHandlerRegistry FromAssembly(Assembly assembly)
    {
        var registrations = new List<MqttHandlerRegistration>();

        foreach (var type in assembly.DefinedTypes.Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            var attributes = type.GetCustomAttributes<MqttTopicAttribute>().ToArray();
            var isHandler = type.IsAssignableTo(typeof(IMqttHandler));

            if (attributes.Length > 0 && !isHandler)
            {
                throw new InvalidOperationException(
                    $"{type.FullName} has [MqttTopic] but does not implement {nameof(IMqttHandler)}.");
            }

            if (isHandler && attributes.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{type.FullName} implements {nameof(IMqttHandler)} but has no [MqttTopic] attribute.");
            }

            registrations.AddRange(attributes.Select(a => new MqttHandlerRegistration(a.Filter, type.AsType())));
        }

        return new MqttHandlerRegistry(registrations);
    }
}

public sealed record MqttHandlerRegistration(string Filter, Type HandlerType);
