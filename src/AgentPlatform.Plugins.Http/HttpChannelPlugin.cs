using System.Collections.Concurrent;
using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Plugins.Http;

/// <summary>GroupId set server-side from the authenticated token — never trusted from the client.</summary>
public record HttpChannelRequest(string MessageId, string UserId, string GroupId, string Text);

/// <summary>Synchronous request/response channel. The HTTP endpoint awaits DeliverAsync. Singleton.</summary>
public sealed class HttpChannelPlugin : IChannelPlugin
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OutputMessage>> _pending = new();

    public string ChannelId => "http";

    public ChannelCapabilities Capabilities => new(
        SupportsInlineButtons: false, SupportsRichCards: false,
        SupportsVoice: false, SupportsAttachments: true);

    public Task<InputMessage> ParseAsync(RawEvent e, CancellationToken ct)
    {
        var req = JsonSerializer.Deserialize<HttpChannelRequest>(e.Body)!;
        return Task.FromResult(new InputMessage(
            req.MessageId, "http", req.UserId,
            string.IsNullOrEmpty(req.GroupId) ? null : req.GroupId,
            req.Text, null, DateTimeOffset.UtcNow));
    }

    public Task DeliverAsync(OutputMessage m, CancellationToken ct)
    {
        var key = m.UserId + ":" + m.RequestId;
        if (_pending.TryRemove(key, out var tcs))
            tcs.TrySetResult(m);
        return Task.CompletedTask;
    }

    public async Task<OutputMessage?> WaitForResponseAsync(string userId, string messageId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<OutputMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = userId + ":" + messageId;
        _pending[key] = tcs;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(90)); // reasoning models (gpt-5/o-series) can be slow
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(key, out _);
            return null;
        }
    }
}
