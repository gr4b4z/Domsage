using AgentPlatform.Infrastructure.Postgres.Entities;

namespace AgentPlatform.Infrastructure.Notifications;

/// <summary>Tunable push-channel priority. <c>email</c> is intentionally NOT here — it has its own fallback path.</summary>
public sealed class NotificationOptions
{
    /// <summary>Messaging channels in preference order; the first one the user has an identity for wins.</summary>
    public string[] ChannelPriority { get; set; } = ["telegram", "signal", "discord"];
}

/// <summary>Deterministic, IO-free selection of a user's single push channel. Replaces the old hardcoded
/// telegram→signal chain so new channels are zero-touch. <c>email</c>/<c>http</c> are never push targets.</summary>
public static class ChannelRouting
{
    public static (string ChannelId, string ExternalId)? SelectPushChannel(
        IEnumerable<ChannelIdentity> identities, IReadOnlyList<string> priority)
    {
        // First identity per messaging channel (stable; mirrors the old FirstOrDefault semantics).
        var byChannel = identities
            .Where(c => c.ChannelId is not ("email" or "http") && !string.IsNullOrEmpty(c.ExternalId))
            .GroupBy(c => c.ChannelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ExternalId, StringComparer.OrdinalIgnoreCase);

        if (byChannel.Count == 0) return null;

        // Configured priority wins…
        foreach (var ch in priority)
            if (byChannel.TryGetValue(ch, out var ext)) return (ch, ext);

        // …then any remaining messaging channel (so an unlisted channel is still reachable — zero-touch).
        var leftover = byChannel.Keys.Except(priority, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        return leftover is null ? null : (leftover, byChannel[leftover]);
    }
}
