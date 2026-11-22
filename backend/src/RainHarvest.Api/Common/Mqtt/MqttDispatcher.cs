namespace RainHarvest.Api.Common.Mqtt;

/// <summary>
/// Routes an incoming message to every handler whose filter matches, each in
/// its own DI scope. A failing handler is logged and does not stop the others.
/// </summary>
public sealed partial class MqttDispatcher(
    MqttHandlerRegistry registry,
    IServiceScopeFactory scopeFactory,
    ILogger<MqttDispatcher> logger)
{
    public async Task DispatchAsync(MqttMessage message, CancellationToken cancellationToken)
    {
        var handled = false;

        foreach (var handlerType in registry.HandlersFor(message.Topic))
        {
            handled = true;
            await using var scope = scopeFactory.CreateAsyncScope();

            try
            {
                var handler = (IMqttHandler)scope.ServiceProvider.GetRequiredService(handlerType);
                await handler.HandleAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogHandlerFailed(ex, handlerType.Name, message.Topic);
            }
        }

        if (!handled)
        {
            LogNoHandler(message.Topic);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "MQTT handler {Handler} failed for topic {Topic}")]
    private partial void LogHandlerFailed(Exception exception, string handler, string topic);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No MQTT handler for topic {Topic}")]
    private partial void LogNoHandler(string topic);
}
