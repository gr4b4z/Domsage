using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using AgentPlatform.Core.Contracts;

namespace AgentPlatform.Infrastructure.Notifications;

/// <summary>Fan-out live events to subscribed web-chat clients over SSE. Singleton, in-memory.</summary>
public sealed class InMemorySseHub : ISseHub
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<string>>> _subs = new();

    public async IAsyncEnumerable<string> Subscribe(string userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true
        });
        var userSubs = _subs.GetOrAdd(userId, _ => new());
        userSubs[id] = ch;
        try
        {
            await foreach (var payload in ch.Reader.ReadAllAsync(ct))
                yield return payload;
        }
        finally
        {
            if (_subs.TryGetValue(userId, out var s)) { s.TryRemove(id, out _); if (s.IsEmpty) _subs.TryRemove(userId, out _); }
        }
    }

    public Task PublishAsync(string userId, LiveEvent evt, CancellationToken ct)
    {
        if (_subs.TryGetValue(userId, out var userSubs))
        {
            var payload = JsonSerializer.Serialize(evt);
            foreach (var ch in userSubs.Values) ch.Writer.TryWrite(payload);
        }
        return Task.CompletedTask;
    }

    // Empty subscriber sets are removed on unsubscribe, so a present non-empty key means a live connection.
    public bool IsConnected(string userId) =>
        _subs.TryGetValue(userId, out var s) && !s.IsEmpty;
}
