using System.Reflection;

namespace RainHarvest.Api.Common.Events;

/// <summary>
/// In-process events between slices (PLAN.md D7): the publisher does not know
/// who listens. Handlers run in the publisher's DI scope, one after another.
/// </summary>
public interface IDomainEventHandler<in TEvent>
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

public sealed partial class DomainEventPublisher(IServiceProvider services, ILogger<DomainEventPublisher> logger)
{
    /// <summary>A failing handler is logged and never fails the publisher or the other handlers.</summary>
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : notnull
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            try
            {
                await handler.HandleAsync(domainEvent, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogHandlerFailed(ex, handler.GetType().Name, typeof(TEvent).Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Handler} failed handling {Event}")]
    private partial void LogHandlerFailed(Exception exception, string handler, string @event);
}

public static class DomainEventExtensions
{
    /// <summary>Registers the publisher and every <see cref="IDomainEventHandler{TEvent}"/> in the assembly (scoped).</summary>
    public static IServiceCollection AddDomainEvents(this IServiceCollection services, Assembly assembly)
    {
        services.AddScoped<DomainEventPublisher>();

        foreach (var type in assembly.DefinedTypes.Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var contract in type.ImplementedInterfaces.Where(i =>
                         i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)))
            {
                services.AddScoped(contract, type);
            }
        }

        return services;
    }
}
