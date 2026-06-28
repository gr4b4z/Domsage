using System.ComponentModel.DataAnnotations.Schema;

namespace AgentPlatform.Infrastructure.Postgres.Entities;

[Table("users")]
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // Email is no longer a column — addresses live in channel_identities (channel_id="email"),
    // one flagged IsPrimary for outbound notifications. A user may have several.
    public string DisplayName { get; set; } = "";
    public string Timezone { get; set; } = "Europe/Warsaw";
    public string? PreferredChannel { get; set; }
    /// <summary>How to deliver notifications: "auto" (SSE→messaging→email fallback), "email" (always also email), "silent" (web SSE only).</summary>
    public string NotifyMode { get; set; } = "auto";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("channel_identities")]
public class ChannelIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ChannelId { get; set; } = "";  // "telegram", "signal", "email" … (a plugin's channel)
    public string ExternalId { get; set; } = ""; // chat id / phone number / email address on that channel
    public Guid UserId { get; set; }
    /// <summary>For channels where a user can have several identities (email): the one used for
    /// agent-initiated outbound (notifications). Irrelevant for single-identity channels.</summary>
    public bool IsPrimary { get; set; }
}

[Table("user_tokens")]
public class UserToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public string Label { get; set; } = "web";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

[Table("groups")]
public class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "household";
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("group_members")]
public class GroupMember
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "member";
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("idempotency_keys")]
public class IdempotencyKey
{
    public string Key { get; set; } = "";
    public string Result { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
}

[Table("pending_intents")]
public class PendingIntentEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? GroupId { get; set; }
    public string IntentId { get; set; } = "";
    public string GatheredSlots { get; set; } = "{}";
    public string[] MissingSlots { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("audit_log")]
public class AuditLogEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }
    public string? GroupType { get; set; }
    public string Intent { get; set; } = "";
    public string PlannerMode { get; set; } = "context_first";
    public string? ToolId { get; set; }
    public string? TargetId { get; set; }
    public string Result { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? IdempotencyKey { get; set; }
    public string PromptVersion { get; set; } = "";
    public string ModelTier { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public int DiagnosticSteps { get; set; }
    public string[]? ContextFetched { get; set; }
    public string Metadata { get; set; } = "{}";
    public string? EvalSignal { get; set; }
    public string? EvalCorrection { get; set; }
    public DateTimeOffset? EvalAt { get; set; }
}

[Table("usage_meter_events")]
public class UsageMeterEventEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }
    public string ProviderId { get; set; } = "";
    public string ModelTier { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CachedTokens { get; set; }
    public decimal CostUsd { get; set; }
    public string? Intent { get; set; }
    public Guid RequestId { get; set; }
}

[Table("budget_states")]
public class BudgetStateEntity
{
    public string ScopeKey { get; set; } = "";
    public decimal SpentUsd { get; set; }
    public bool Tripped { get; set; }
    public DateTimeOffset? TrippedAt { get; set; }
    public DateTimeOffset? ResetAt { get; set; }
    public DateTimeOffset WindowStart { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("scheduler_jobs")]
public class SchedulerJobEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? GroupId { get; set; }
    public Guid? UserId { get; set; }
    public string JobType { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public string? RRule { get; set; }
    public string Timezone { get; set; } = "Europe/Warsaw";
    public DateTimeOffset NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A generic "if-this-then-that" rule: on a schedule, run a read-only tool, evaluate a deterministic
/// condition on its result, and notify the owner only when it holds. The recurring run uses NO LLM —
/// the LLM authors the rule once, the engine executes it deterministically.
/// </summary>
[Table("automation_rules")]
public class AutomationRuleEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? GroupId { get; set; }
    public string Description { get; set; } = "";
    public string RRule { get; set; } = "FREQ=DAILY";
    public string Timezone { get; set; } = "Europe/Warsaw";
    public DateTimeOffset NextRunAt { get; set; }
    /// <summary>Read-only tool to run as the check, e.g. "weather.current".</summary>
    public string ToolId { get; set; } = "";
    /// <summary>JSON arguments for the check tool.</summary>
    public string ToolInput { get; set; } = "{}";
    /// <summary>Dot-path into the tool result, e.g. "Days.1.PrecipProb".</summary>
    public string ConditionPath { get; set; } = "";
    /// <summary>One of: &gt;= &lt;= &gt; &lt; == != contains.</summary>
    public string ConditionOp { get; set; } = ">=";
    public string ConditionValue { get; set; } = "";
    /// <summary>Notification text when the condition holds. May use {value} for the matched value.</summary>
    public string MessageText { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>Last time the check ran (regardless of outcome).</summary>
    public DateTimeOffset? LastFiredAt { get; set; }
    /// <summary>Last time the condition held and a notification was sent.</summary>
    public DateTimeOffset? LastTriggeredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("pending_confirmations")]
public class PendingConfirmationEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? GroupId { get; set; }
    public string ChannelId { get; set; } = "";
    public string ActionPlan { get; set; } = "{}";
    public string? MessageId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(10);
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? EvalSignal { get; set; }
    public string? EvalCorrection { get; set; }
    public DateTimeOffset? EvalAt { get; set; }
}

[Table("dead_letter_queue")]
public class DeadLetterEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? GroupId { get; set; }
    public string ToolId { get; set; } = "";
    public string Input { get; set; } = "{}";
    public string ErrorMessage { get; set; } = "";
    public string ErrorType { get; set; } = "";
    public int RetryCount { get; set; }
    public bool Resolved { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

[Table("prompt_versions")]
public class PromptVersionEntity
{
    public string Id { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string Content { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string ModelTier { get; set; } = "";
    public double Temperature { get; set; } = 0.2;
    public double? TopP { get; set; }
    public int? MaxTokens { get; set; }
    public string? ReasoningLevel { get; set; }
    public string ProviderId { get; set; } = "";
    public string? CacheKey { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? PromotedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Conversation tables — kept in public schema (shared across plugins).
[Table("conversations")]
public class ConversationEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? GroupId { get; set; }
    public string ChannelId { get; set; } = "";
    public bool Incognito { get; set; }
    public string Status { get; set; } = "active";
    public string? CloseReason { get; set; }
    public string? Summary { get; set; }
    public Guid? SummaryCoversUpTo { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActiveAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }
}

[Table("conversation_messages")]
public class ConversationMessageEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Intent { get; set; }
    public string? ActionSummary { get; set; }
    public int Tokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("memory_facts")]
public class MemoryFactEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? GroupId { get; set; }
    public Guid? UserId { get; set; }
    public string Scope { get; set; } = "user";
    public string Category { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Source { get; set; } = "";
    public double? Confidence { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
