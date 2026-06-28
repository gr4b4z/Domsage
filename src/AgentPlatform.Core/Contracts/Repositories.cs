using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Core.Contracts;

// ── Audit ─────────────────────────────────────────────────────────────────────

public record AuditEntry(
    string? UserId,
    string? GroupId,
    string? GroupType,
    string Intent,
    string PlannerMode,
    string? ToolId,
    string? TargetId,
    string Result,
    string? ErrorMessage,
    string? IdempotencyKey,
    string PromptVersion,
    string ModelTier,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    int DiagnosticSteps,
    string[]? ContextFetched);

public record AuditActionRecord(string Intent, string? ToolId, string? TargetId, string Result, DateTimeOffset OccurredAt);

public interface IAuditLogRepository
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct);

    /// <summary>Secondary history source — finds past actions even after a conversation reset.</summary>
    Task<IReadOnlyList<AuditActionRecord>> SearchActionsAsync(
        string? groupId, string query, int limit, CancellationToken ct);
}

// ── Idempotency ─────────────────────────────────────────────────────────────

public interface IIdempotencyRepository
{
    /// <summary>Returns true if the key was newly acquired; false if it already exists.</summary>
    Task<bool> TryAcquireAsync(string key, CancellationToken ct);
    Task<ToolResult> GetCachedResultAsync(string key, CancellationToken ct);
    Task StoreResultAsync(string key, ToolResult result, CancellationToken ct);
    Task ReleaseAsync(string key, CancellationToken ct);
}

// ── Pending intents (clarify) ─────────────────────────────────────────────────

public interface IPendingIntentRepository
{
    Task<PendingIntent?> GetActiveAsync(string userId, CancellationToken ct);
    Task SaveAsync(PendingIntent intent, CancellationToken ct);
    Task ClearAsync(Guid id, CancellationToken ct);
}

// ── Pending confirmations ─────────────────────────────────────────────────────

public record PendingConfirmation(string Id, ActionPlan Plan);

public interface IPendingConfirmationRepository
{
    Task<string> SaveAsync(ActionPlan plan, ExecutionContext ctx, CancellationToken ct);
    Task<PendingConfirmation?> GetAsync(string id, CancellationToken ct);
    Task RecordSignalAsync(string id, string signal, string? correction, CancellationToken ct);
    Task ExpireAsync(string id, CancellationToken ct);
}

// ── Budget state ─────────────────────────────────────────────────────────────

public record BudgetState(string ScopeKey, decimal SpentUsd, bool Tripped);

public interface IBudgetRepository
{
    Task<BudgetState?> GetAsync(string scopeKey, CancellationToken ct);
    Task RecordSpendAsync(string scopeKey, decimal cost, decimal cap, CancellationToken ct);
    Task ResetAsync(string scopeKey, CancellationToken ct);
}

// ── Dead letter queue ─────────────────────────────────────────────────────────

public interface IDeadLetterRepository
{
    Task WriteAsync(string? groupId, string toolId, string inputJson, string errorMessage,
        string errorType, CancellationToken ct);
}

// ── Prompt versions ─────────────────────────────────────────────────────────

public record PromptVersionRecord(
    string Id,
    string TemplateId,
    string Content,
    string ModelId,
    string ModelTier,
    double Temperature,
    double? TopP,
    int? MaxTokens,
    string? ReasoningLevel,
    string ProviderId);

public interface IPromptVersionRepository
{
    /// <summary>Returns the active prompt version for a template id, or null if none stored.</summary>
    Task<PromptVersionRecord?> GetActiveAsync(string templateId, CancellationToken ct);
}
