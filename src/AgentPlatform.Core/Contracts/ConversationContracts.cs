using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Core.Contracts;

public record ConversationRecord(
    Guid Id,
    string UserId,
    string? GroupId,
    string ChannelId,
    bool Incognito,
    string Status,
    string? Summary,
    Guid? SummaryCoversUpTo,
    DateTime LastActiveAt);

public record ConversationMessageRecord(
    string Role,
    string Content,
    string? Intent = null,
    string? ActionSummary = null,
    int Tokens = 0);

public interface IConversationRepository
{
    Task<ConversationRecord?> GetActiveAsync(string userId, string channelId, CancellationToken ct);
    Task<ConversationRecord> GetAsync(string conversationId, CancellationToken ct);
    Task<ConversationRecord> CreateAsync(string userId, string? groupId, string channelId,
        bool isIncognito, CancellationToken ct);
    Task CloseAsync(string conversationId, string reason, CancellationToken ct);
    Task AppendAsync(string conversationId, ConversationMessageRecord message, CancellationToken ct);
    Task AppendMetadataOnlyAsync(string conversationId, int tokens, CancellationToken ct);
    Task TouchAsync(string conversationId, CancellationToken ct);
    Task SaveSummaryAsync(string conversationId, string summary, Guid coversUpTo, CancellationToken ct);
    Task<IReadOnlyList<ConversationMessageRecord>> FetchWithinBudgetAsync(
        string conversationId, Guid? afterMessageId, int tokenBudget, CancellationToken ct);

    /// <summary>Full-text search (tsvector) over this user's conversation messages.</summary>
    Task<IReadOnlyList<ConversationMessageRecord>> SearchMessagesAsync(
        string userId, string query, int limit, CancellationToken ct);
}
