using AgentPlatform.Core.Contracts;

namespace AgentPlatform.Core.Pipeline;

/// <summary>
/// The only place that writes conversation content. In incognito, writes metadata only. Scoped.
/// </summary>
public sealed class ConversationWriter(IConversationRepository repo)
{
    public async Task WriteAsync(
        string conversationId,
        bool isIncognito,
        string userText,
        string assistantText,
        string? intent,
        string? actionSummary,
        int tokens,
        CancellationToken ct)
    {
        if (isIncognito)
        {
            await repo.AppendMetadataOnlyAsync(conversationId, tokens, ct);
            return;
        }

        await repo.AppendAsync(conversationId,
            new ConversationMessageRecord("user", userText, Intent: intent, Tokens: tokens / 2), ct);
        await repo.AppendAsync(conversationId,
            new ConversationMessageRecord("assistant", assistantText, ActionSummary: actionSummary, Tokens: tokens / 2), ct);
        await repo.TouchAsync(conversationId, ct);
    }
}
