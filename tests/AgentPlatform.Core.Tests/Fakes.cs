using AgentPlatform.Core.Contracts;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.Core.Tests;

public sealed class FakeBudgetRepository : IBudgetRepository
{
    public Dictionary<string, BudgetState> States { get; } = new();
    public Task<BudgetState?> GetAsync(string scopeKey, CancellationToken ct) =>
        Task.FromResult(States.GetValueOrDefault(scopeKey));
    public Task RecordSpendAsync(string scopeKey, decimal cost, decimal cap, CancellationToken ct)
    {
        var cur = States.GetValueOrDefault(scopeKey)?.SpentUsd ?? 0m;
        var spent = cur + cost;
        States[scopeKey] = new BudgetState(scopeKey, spent, spent >= cap);
        return Task.CompletedTask;
    }
    public Task ResetAsync(string scopeKey, CancellationToken ct)
    {
        States[scopeKey] = new BudgetState(scopeKey, 0m, false);
        return Task.CompletedTask;
    }
}

public sealed class FakeIdempotencyRepository : IIdempotencyRepository
{
    private readonly Dictionary<string, ToolResult?> _store = new();
    public int ExecuteCount;
    public Task<bool> TryAcquireAsync(string key, CancellationToken ct)
    {
        if (_store.ContainsKey(key)) return Task.FromResult(false);
        _store[key] = null;
        return Task.FromResult(true);
    }
    public Task<ToolResult> GetCachedResultAsync(string key, CancellationToken ct) =>
        Task.FromResult(_store[key] ?? new ToolResult(ToolResultStatus.Success, null, null, "cached"));
    public Task StoreResultAsync(string key, ToolResult result, CancellationToken ct)
    { _store[key] = result; return Task.CompletedTask; }
    public Task ReleaseAsync(string key, CancellationToken ct) { _store.Remove(key); return Task.CompletedTask; }
}

public sealed class FakeAuditRepository : IAuditLogRepository
{
    public List<AuditEntry> Entries { get; } = new();
    public Task WriteAsync(AuditEntry entry, CancellationToken ct) { Entries.Add(entry); return Task.CompletedTask; }
    public Task<IReadOnlyList<AuditActionRecord>> SearchActionsAsync(string? g, string q, int l, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AuditActionRecord>>([]);
}

public sealed class FakeDeadLetterRepository : IDeadLetterRepository
{
    public int Count;
    public Task WriteAsync(string? g, string t, string i, string e, string et, CancellationToken ct)
    { Count++; return Task.CompletedTask; }
}

public sealed class FakeLlmProvider(string? content, ToolCallRequest? toolCall = null) : ILlmProvider
{
    public int Calls;
    public string ProviderId => "fake";
    public ModelTier Tier => ModelTier.Small;
    public PriceCard Price => new(0m, 0m, 0m);
    public Task<LlmResult> CompleteAsync(LlmRequest req, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(new LlmResult(content, 10, 5, 0, toolCall));
    }
}

public sealed class FakeTool(string toolId, bool sideEffects, MemberRole minRole = MemberRole.Member) : ITool
{
    public int Executions;
    public string ToolId => toolId;
    public bool HasSideEffects => sideEffects;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, minRole);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object).Build();
    public Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        Executions++;
        return Task.FromResult(new ToolResult(ToolResultStatus.Success, null, null, "done"));
    }
}

public sealed class FakeHandler(string intentId, string[] allowedTools,
    PlannerMode mode = PlannerMode.ContextFirst,
    ConfirmationPolicy confirm = ConfirmationPolicy.NotRequired) : IIntentHandler
{
    public string IntentId => intentId;
    public PlannerMode Mode => mode;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => allowedTools;
    public string PromptTemplateId => intentId;
    public ModelTier PreferredTier => ModelTier.Small;
    public ConfirmationPolicy Confirmation => confirm;
}

public sealed class FakeConversationRepository : IConversationRepository
{
    public List<ConversationRecord> Created { get; } = new();
    public List<(string id, string reason)> Closed { get; } = new();
    public ConversationRecord? Active { get; set; }

    public Task<ConversationRecord?> GetActiveAsync(string userId, string channelId, CancellationToken ct) =>
        Task.FromResult(Active);
    public Task<ConversationRecord> GetAsync(string id, CancellationToken ct) =>
        Task.FromResult(Active ?? Make(false));
    public Task<ConversationRecord> CreateAsync(string userId, string? groupId, string channelId, bool incognito, CancellationToken ct)
    {
        var rec = Make(incognito);
        Created.Add(rec);
        Active = rec;
        return Task.FromResult(rec);
    }
    public Task CloseAsync(string id, string reason, CancellationToken ct) { Closed.Add((id, reason)); return Task.CompletedTask; }
    public Task AppendAsync(string id, ConversationMessageRecord m, CancellationToken ct) => Task.CompletedTask;
    public Task AppendMetadataOnlyAsync(string id, int tokens, CancellationToken ct) => Task.CompletedTask;
    public Task TouchAsync(string id, CancellationToken ct) => Task.CompletedTask;
    public Task SaveSummaryAsync(string id, string summary, Guid cursor, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ConversationMessageRecord>> FetchWithinBudgetAsync(string id, Guid? after, int budget, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ConversationMessageRecord>>([]);
    public Task<IReadOnlyList<ConversationMessageRecord>> SearchMessagesAsync(string userId, string query, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ConversationMessageRecord>>([]);

    private static ConversationRecord Make(bool incognito) =>
        new(Guid.NewGuid(), Guid.NewGuid().ToString(), null, "test", incognito, "active", null, null, DateTime.UtcNow);
}

public static class TestCtx
{
    public static ExecutionContext Make(bool incognito = false, MemberRole role = MemberRole.Member) =>
        new("req-1", Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "household", role,
            "test", Guid.NewGuid().ToString(), incognito, DateTimeOffset.UtcNow);
}
