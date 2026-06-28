using System.Text.Json;

namespace AgentPlatform.PluginSdk.Contracts.Models;

// ── Channel I/O ───────────────────────────────────────────────────────────────

public record RawEvent(string ChannelId, string Body);

public record Attachment(string StorageRef, string MediaType, long SizeBytes);

public record InputMessage(
    string MessageId,
    string ChannelId,
    string UserId,
    string? GroupId,
    string Text,
    Attachment? Attachment,
    DateTimeOffset ReceivedAt
);

public record OutputMessage(
    string ChannelId,
    string UserId,
    string Text,
    bool ConfirmationRequired,
    string? ConfirmationId,
    IReadOnlyList<string>? Actions
)
{
    /// <summary>Optional request correlation id used by synchronous channels (HTTP).</summary>
    public string? RequestId { get; init; }
}

public record ChannelCapabilities(
    bool SupportsInlineButtons,
    bool SupportsRichCards,
    bool SupportsVoice,
    bool SupportsAttachments
);

// ── Execution / context ───────────────────────────────────────────────────────

public record ExecutionContext(
    string RequestId,
    string UserId,
    string GroupId,
    string GroupType,
    MemberRole UserRole,
    string ChannelId,
    string ConversationId,
    bool IsIncognito,
    DateTimeOffset StartedAt,
    ExecutionMode Mode = ExecutionMode.Normal
);

public record ContextRequest(
    ExecutionContext ExecutionContext,
    string IntentId,
    string UserText
);

public record ContextSlice(string ProviderId, ContextScope Scope, object Data)
{
    public static ContextSlice Empty { get; } =
        new("__empty__", ContextScope.User, new { });
}

public record AgentContext(
    IReadOnlyList<ContextSlice> Slices,
    IReadOnlyList<string> FetchedProviderIds
)
{
    public string ToPromptJson() =>
        JsonSerializer.Serialize(Slices.ToDictionary(s => s.ProviderId, s => s.Data));
}

// ── Plan & tool I/O ─────────────────────────────────────────────────────────

public record ActionPlan(
    string Intent,
    PlannerMode Mode,
    ContextScope Scope,
    string? TargetId,
    string ToolId,
    double Confidence,
    bool RequiresConfirmation,
    string IdempotencyKey,
    string PromptVersion,
    ModelTier ModelTier,
    int DiagnosticSteps,
    JsonElement ToolInput
);

public record ToolInput(string ToolId, JsonElement Arguments);

public record ToolResult(
    ToolResultStatus Status,
    JsonElement? Data,
    string? ErrorMessage,
    string? HumanMessage = null
);

public record ResponseResult(
    string Text,
    bool RequiresConfirmation,
    string? ConfirmationId,
    IReadOnlyList<string>? Actions
);

// ── LLM ──────────────────────────────────────────────────────────────────────

public record LlmMessage(string Role, string Content);

public record ToolSchema(string Name, string Description, string JsonSchema);

public record LlmRequest(
    string ModelId,
    ModelTier Tier,
    double Temperature,
    double? TopP,
    int? MaxTokens,
    string? ReasoningLevel,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<ToolSchema>? Tools
);

public record ToolCallRequest(string ToolName, JsonElement Arguments);

public record LlmResult(
    string? Content,
    int InputTokens,
    int OutputTokens,
    int CachedTokens,
    ToolCallRequest? ToolCall
);

public record PriceCard(
    decimal InputPerMillion,
    decimal OutputPerMillion,
    decimal CachedPerMillion
);

// ── Usage / budget ─────────────────────────────────────────────────────────

public record ScopeRequirement(ContextScope Scope, MemberRole MinimumRole);

public record BudgetScope(string Key);

public record Window(DateTimeOffset From, DateTimeOffset To);

public record UsageEvent(
    string RequestId,
    string UserId,
    string? GroupId,
    string ProviderId,
    ModelTier Tier,
    string PromptVersion,
    int InputTokens,
    int OutputTokens,
    int CachedTokens,
    decimal CostUsd,
    string? Intent
);

public record SpendSnapshot(BudgetScope Scope, decimal SpentUsd, bool IsTripped);

// ── Pipeline hook ─────────────────────────────────────────────────────────

public record PipelineRunResult(
    string RequestId,
    string UserId,
    string? GroupId,
    string ChannelId,
    string Intent,
    PipelineRunStatus Status,
    string? ToolId,
    double? Confidence,
    string? ErrorMessage,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    TimeSpan Duration,
    DateTimeOffset CompletedAt
);

// ── User resolution ─────────────────────────────────────────────────────────

public record UserGroupInfo(
    string UserId, string GroupId, string GroupType, MemberRole Role, string DisplayName);
