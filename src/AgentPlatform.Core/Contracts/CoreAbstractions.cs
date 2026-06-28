using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Core.Contracts;

// ── Exceptions ────────────────────────────────────────────────────────────────

public sealed class BudgetExceededException(string message) : Exception(message);

public sealed class RetryableToolException(string? message = null) : Exception(message ?? "Tool failed, retryable");

public sealed class TerminalToolException(string message, string userMessage) : Exception(message)
{
    public string UserMessage { get; } = userMessage;
}

public sealed class ToolInputValidationException(IEnumerable<string>? errors = null)
    : Exception("Tool input failed schema validation: " + string.Join("; ", errors ?? []));

public sealed class SecurityViolationException(string message) : Exception(message);

public sealed class ContextScopeViolationException(string message) : Exception(message);

public sealed class PluginConflictException(string message) : Exception(message);

// ── Execution context accessor (for RLS interceptor) ──────────────────────────

public interface IExecutionContextAccessor
{
    ExecutionContext? Current { get; set; }
}

// ── Scheduler (interface in Core; impl in Infrastructure) ─────────────────────

public record SchedulerJob(
    Guid Id,
    string? GroupId,
    string? UserId,
    string JobType,
    string PayloadJson,
    string? RRule,
    string Timezone,
    DateTimeOffset NextRunAt);

public interface ISchedulerService
{
    Task ScheduleAsync(SchedulerJob job, CancellationToken ct);
    Task CancelAsync(Guid jobId, CancellationToken ct);
}

// ── Pipeline supporting types ─────────────────────────────────────────────────

public record IntentMatch(
    string IntentId,
    double Confidence,
    string RawSegment,
    IReadOnlyList<string> MissingSlots);

public record ConversationContext(string ConversationId, bool IsIncognito, string? Summary);

public record ValidationResult(
    bool RequiresConfirmation,
    ActionPlan Plan,
    string? ConfirmationPrompt = null,
    bool Rejected = false,
    string? RejectionReason = null);

public record PendingIntent(
    string UserId,
    string? GroupId,
    string IntentId,
    IReadOnlyDictionary<string, string> GatheredSlots,
    IReadOnlyList<string> MissingSlots)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}
