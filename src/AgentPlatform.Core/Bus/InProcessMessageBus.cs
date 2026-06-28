using System.Threading.Channels;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Core.Bus;

/// <summary>In-process bus backed by a bounded Channel. Singleton.</summary>
public sealed class InProcessMessageBus : IMessageBus
{
    private readonly Channel<RawEvent> _channel =
        Channel.CreateBounded<RawEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    public Task PublishAsync(RawEvent e, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(e, ct).AsTask();

    internal IAsyncEnumerable<RawEvent> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
