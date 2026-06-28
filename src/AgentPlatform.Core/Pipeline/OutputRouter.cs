using AgentPlatform.Core.Registry;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Core.Pipeline;

/// <summary>Routes an OutputMessage to the channel it came from (or a fallback). Scoped.</summary>
public sealed class OutputRouter(PluginRegistry registry)
{
    public Task DeliverAsync(OutputMessage message, CancellationToken ct)
    {
        var channel = registry.GetChannel(message.ChannelId);
        return channel.DeliverAsync(message, ct);
    }
}
