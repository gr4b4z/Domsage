namespace AgentPlatform.Core.Contracts;

public record GroupMember(string UserId, string DisplayName, string? PreferredChannel);

/// <summary>Resolves group members and matches them by name (for "going with Agatha and Ola").</summary>
public interface IGroupDirectory
{
    Task<IReadOnlyList<GroupMember>> GetMembersAsync(string groupId, CancellationToken ct);

    /// <summary>Best-effort name match within a group (case-insensitive, prefix/contains).</summary>
    Task<IReadOnlyList<GroupMember>> ResolveByNamesAsync(
        string groupId, IEnumerable<string> names, CancellationToken ct);
}

/// <summary>
/// A live event pushed to web-chat clients over Server-Sent Events. May optionally carry an
/// <b>ack action</b>: a tool to run (by id, via the generic /api/action path) when the user taps
/// the confirm button — e.g. "✅ Zapłacone" → family.payments.mark_paid. This is how a scheduled
/// notification "requires confirmation in this cycle" without the core knowing the domain: it just
/// dispatches the stored tool id. ActionInput is a JSON object string passed verbatim as the tool input.
/// </summary>
public record LiveEvent(string Type, string Title, string Body, string? Json = null,
    string? ActionToolId = null, string? ActionInput = null, string? ActionLabel = null);

/// <summary>In-memory SSE hub — fan-out live events to subscribed users (web chat). Singleton.</summary>
public interface ISseHub
{
    /// <summary>Subscribe a user; returns an async stream of serialized SSE 'data:' payloads.</summary>
    IAsyncEnumerable<string> Subscribe(string userId, CancellationToken ct);
    Task PublishAsync(string userId, LiveEvent evt, CancellationToken ct);

    /// <summary>True if the user currently has at least one live web-chat (SSE) connection.</summary>
    bool IsConnected(string userId);
}

/// <summary>
/// Delivers a notification to a set of users: live (SSE) for web chat + best-effort push on
/// each user's messaging channel (Telegram/Signal). Implemented in Infrastructure.
/// </summary>
public interface INotificationService
{
    Task NotifyUsersAsync(IEnumerable<string> userIds, LiveEvent evt, CancellationToken ct);
}
