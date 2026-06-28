namespace AgentPlatform.Core.Contracts;

public record GroupMember(string UserId, string DisplayName, long? TelegramId,
    string? SignalNumber, string? Email, string? PreferredChannel);

/// <summary>Resolves group members and matches them by name (for "going with Agatha and Ola").</summary>
public interface IGroupDirectory
{
    Task<IReadOnlyList<GroupMember>> GetMembersAsync(string groupId, CancellationToken ct);

    /// <summary>Best-effort name match within a group (case-insensitive, prefix/contains).</summary>
    Task<IReadOnlyList<GroupMember>> ResolveByNamesAsync(
        string groupId, IEnumerable<string> names, CancellationToken ct);
}

/// <summary>A live event pushed to web-chat clients over Server-Sent Events.</summary>
public record LiveEvent(string Type, string Title, string Body, string? Json = null);

/// <summary>In-memory SSE hub — fan-out live events to subscribed users (web chat). Singleton.</summary>
public interface ISseHub
{
    /// <summary>Subscribe a user; returns an async stream of serialized SSE 'data:' payloads.</summary>
    IAsyncEnumerable<string> Subscribe(string userId, CancellationToken ct);
    Task PublishAsync(string userId, LiveEvent evt, CancellationToken ct);
}

/// <summary>
/// Delivers a notification to a set of users: live (SSE) for web chat + best-effort push on
/// each user's messaging channel (Telegram/Signal). Implemented in Infrastructure.
/// </summary>
public interface INotificationService
{
    Task NotifyUsersAsync(IEnumerable<string> userIds, LiveEvent evt, CancellationToken ct);
}
