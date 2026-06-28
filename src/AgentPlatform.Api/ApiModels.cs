namespace AgentPlatform.Api;

// Request bodies — GroupId/UserId never trusted from client; resolved from token.
public record HttpChatBody(string Text);
public record HttpConfirmBody(string ConfirmationId, bool Confirmed);
public record HttpChatResponse(string Text, bool RequiresConfirmation, string? ConfirmationId);
public record SetupInitRequest(string Name, string GroupName);
public record BudgetResetRequest(string ScopeKey);

// Generic deterministic action — invoke any plugin tool by id (no LLM). Used by plugin UIs.
public record ActionRequest(string Tool, System.Text.Json.JsonElement Input);

// Self-service email linking (verified by a code mailed to the address).
public record EmailLinkRequest(string Address);
public record EmailLinkConfirm(string Code);

// Stats SQL projections.
public record LlmDailyRow(DateOnly Day, int Calls, long InputTokens, long OutputTokens, decimal CostUsd, string ModelTier);
public record ToolStatsRow(string ToolId, int TotalCalls, int Successes, int Failures, decimal AvgCostUsd);
public record IntentQualityRow(string Intent, int Total, int Accepted, int Corrected, int Cancelled, int Ignored, decimal AvgCostUsd);
public record BreakerRow(string ScopeKey, decimal SpentUsd, bool Tripped, DateTimeOffset? TrippedAt, DateTimeOffset WindowStart);
public record DlqRow(string ToolId, string ErrorType, int Count, DateTimeOffset LastAt);
