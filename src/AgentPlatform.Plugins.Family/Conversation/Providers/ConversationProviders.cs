using AgentPlatform.Core.Contracts;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Plugins.Family.Conversation.Providers;

/// <summary>conversation.history — recent messages within a token budget (empty in incognito).</summary>
public sealed class ConversationHistoryProvider(IConversationRepository repo) : IContextProvider
{
    private const int TokenBudget = 2000;
    public string ProviderId => "conversation.history";
    public ContextScope Scope => ContextScope.User;

    public async Task<ContextSlice> FetchAsync(ContextRequest req, CancellationToken ct)
    {
        if (req.ExecutionContext.IsIncognito)
            return ContextSlice.Empty;

        var conv = await repo.GetAsync(req.ExecutionContext.ConversationId, ct);
        var messages = await repo.FetchWithinBudgetAsync(
            conv.Id.ToString(), conv.SummaryCoversUpTo, TokenBudget, ct);

        return new ContextSlice(ProviderId, Scope, new
        {
            summary = conv.Summary,
            messages = messages.Select(m => new { role = m.Role, content = m.Content })
        });
    }
}

/// <summary>user.memory — long-term facts (empty in incognito).</summary>
public sealed class UserMemoryProvider(IMemoryFactsRepository repo) : IContextProvider
{
    public string ProviderId => "user.memory";
    public ContextScope Scope => ContextScope.User;

    public async Task<ContextSlice> FetchAsync(ContextRequest req, CancellationToken ct)
    {
        if (req.ExecutionContext.IsIncognito)
            return ContextSlice.Empty;

        var facts = await repo.GetForUserAsync(
            req.ExecutionContext.UserId, req.ExecutionContext.GroupId, ct);
        return new ContextSlice(ProviderId, Scope, new
        {
            facts = facts.Select(f => new { f.Key, f.Value, f.Category })
        });
    }
}
