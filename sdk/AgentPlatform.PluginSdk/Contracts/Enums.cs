namespace AgentPlatform.PluginSdk.Contracts;

/// <summary>How the Planner should execute an intent.</summary>
public enum PlannerMode { ContextFirst, ToolCalling }

/// <summary>Whether a planned action requires user confirmation before execution.</summary>
public enum ConfirmationPolicy { NotRequired, Required, RequiredForHighImpact }

/// <summary>Scope a context provider or tool operates in.</summary>
public enum ContextScope { User, Group }

/// <summary>
/// Core roles. Plugins may define additional role strings (e.g. 'child', 'owner')
/// mapped to one of these by an IGroupTypeProvider.
/// </summary>
public enum MemberRole { Guest = 0, Member = 1, Admin = 2 }

/// <summary>Model capability/cost tier. Tier 0 = cheapest local; tier 3 = most capable.</summary>
public enum ModelTier { Local = 0, Small = 1, Medium = 2, Large = 3 }

/// <summary>Outcome of a tool execution.</summary>
public enum ToolResultStatus { Success, Retryable, Failed }

/// <summary>Terminal status of a single pipeline run, reported to pipeline hooks.</summary>
public enum PipelineRunStatus { Success, Failed, Rejected, Clarify, BudgetExceeded, Incognito }

/// <summary>
/// Normal: full Validator + idempotency.
/// Diagnostic: tool calls inside a ToolCalling loop — no Validator, no idempotency.
/// </summary>
public enum ExecutionMode { Normal, Diagnostic }
