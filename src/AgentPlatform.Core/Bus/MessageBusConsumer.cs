using AgentPlatform.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Core.Bus;

/// <summary>Reads events from the bus and drives one pipeline run per message, in a DI scope.</summary>
public sealed class MessageBusConsumer(
    InProcessMessageBus bus,
    IServiceScopeFactory scopeFactory,
    ILogger<MessageBusConsumer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var evt in bus.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<AgentPipeline>();
                await pipeline.RunAsync(evt, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Unhandled error processing event from channel {Channel}", evt.ChannelId);
            }
        }
    }
}
