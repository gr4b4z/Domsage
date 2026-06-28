using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.PluginSdk.Contracts;

/// <summary>Converts a channel-specific message into a common InputMessage and renders back.</summary>
public interface IChannelPlugin
{
    string ChannelId { get; }
    Task<InputMessage> ParseAsync(RawEvent e, CancellationToken ct);
    Task DeliverAsync(OutputMessage m, CancellationToken ct);
    ChannelCapabilities Capabilities { get; }
}

/// <summary>Deterministic action with an explicit, validated input schema. Never calls an LLM.</summary>
public interface ITool
{
    string ToolId { get; }
    JsonSchema InputSchema { get; }
    ScopeRequirement RequiredScope { get; }
    bool HasSideEffects { get; }
    Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct);

    /// <summary>
    /// Optional human-readable preview shown when this tool requires confirmation, built from the
    /// proposed input (e.g. "Utworzę regułę: codziennie o 7:00…"). Default null → a generic prompt.
    /// </summary>
    string? ConfirmationPreview(System.Text.Json.JsonElement input) => null;
}

/// <summary>Purely declarative — tells the Planner what an intent needs. Does not call the LLM.</summary>
public interface IIntentHandler
{
    string IntentId { get; }
    PlannerMode Mode { get; }
    string[] RequiredContextProviders { get; }
    string[] AllowedTools { get; }
    string PromptTemplateId { get; }
    ModelTier PreferredTier { get; }
    ConfirmationPolicy Confirmation { get; }
    string? CapabilityId => null;

    /// <summary>
    /// Optional one-line hint shown to the intent router so it can match this intent more reliably
    /// (e.g. "recurring monitoring: 'sprawdzaj codziennie…', 'powiadom mnie gdy…'"). Default null →
    /// the router sees only the intent id. Keep it short; it is added to every routing prompt.
    /// </summary>
    string? Description => null;

    /// <summary>
    /// When true, the tool's structured result is rephrased by a small LLM into a natural answer to the
    /// user's actual question (instead of returning the tool's fixed message). For Q&amp;A-style intents
    /// (weather, web answers). Default false — commands/actions keep their concise deterministic reply.
    /// </summary>
    bool PhraseResult => false;
}

/// <summary>Supplies a scoped slice of state for the Context Builder.</summary>
public interface IContextProvider
{
    string ProviderId { get; }
    ContextScope Scope { get; }
    Task<ContextSlice> FetchAsync(ContextRequest req, CancellationToken ct);
}

/// <summary>Model behind an abstraction so providers and tiers are swappable.</summary>
public interface ILlmProvider
{
    string ProviderId { get; }
    ModelTier Tier { get; }
    PriceCard Price { get; }
    Task<LlmResult> CompleteAsync(LlmRequest req, CancellationToken ct);
}

/// <summary>Wraps every LLM call. Records real usage and is the source budget checks read.</summary>
public interface IUsageMeter
{
    Task RecordAsync(UsageEvent e, CancellationToken ct);
    Task<SpendSnapshot> GetSpendAsync(BudgetScope scope, Window window, CancellationToken ct);
}

/// <summary>In-process for MVP; swappable to Service Bus without touching the pipeline.</summary>
public interface IMessageBus
{
    Task PublishAsync(RawEvent e, CancellationToken ct = default);
}

/// <summary>Invoice documents and attachments. Local disk in MVP; Azure Blob in prod.</summary>
public interface IBlobStorage
{
    Task<string> StoreAsync(Stream data, string mediaType, CancellationToken ct);
    Task<Stream> ReadAsync(string storageRef, CancellationToken ct);
}

/// <summary>Maps external identifiers to internal user+group. Used by pipeline and channel plugins.</summary>
public interface IUserRepository
{
    /// <summary>Resolves a user by a messaging-channel identity (e.g. ("telegram","12345")). Core stays
    /// channel-agnostic — any plugin channel uses the same generic lookup, no per-channel column or method.</summary>
    Task<UserGroupInfo?> GetByChannelIdentityAsync(string channelId, string externalId, CancellationToken ct);

    Task<UserGroupInfo?> GetPrimaryGroupAsync(string userId, CancellationToken ct);

    /// <summary>Binds a single-identity channel (telegram/signal) to a user — replaces any prior identity
    /// for that (user, channel). One external id maps to one user. Returns false if the user is unknown.</summary>
    Task<bool> SetChannelIdentityAsync(string userId, string channelId, string externalId, CancellationToken ct);

    /// <summary>Adds an email address to a user (a user may have several). The first becomes primary
    /// (used for agent-initiated outbound); pass makePrimary to promote a later one. An address maps to
    /// exactly one user (reassigned if already taken). Returns false if the user is unknown.</summary>
    Task<bool> AddEmailIdentityAsync(string userId, string address, bool makePrimary, CancellationToken ct);
}

/// <summary>Plugin-defined group types. Core only knows groups.type is a string.</summary>
public interface IGroupTypeProvider
{
    string GroupType { get; }
    string[] KnownRoles { get; }
    MemberRole MapToCore(string role);
}

/// <summary>Observer called after every pipeline run. Cannot modify the result or block the pipeline.</summary>
public interface IPipelineHook
{
    Task OnCompletedAsync(PipelineRunResult result, CancellationToken ct);
}

/// <summary>Deferred — MVP4+. Reserved so the abstraction boundary is established now.</summary>
public interface ISemanticMemory
{
    Task<string> StoreAsync(SemanticDocument doc, CancellationToken ct);
    Task<IReadOnlyList<SemanticChunk>> SearchAsync(string query, ContextScope scope,
        string scopeId, int topK, CancellationToken ct);
}

public record SemanticDocument(
    string ScopeId, ContextScope Scope, string Category, string Content,
    string SourceRef, DateTimeOffset CreatedAt);

public record SemanticChunk(string Content, double Score, string SourceRef);

/// <summary>Organizational layer — resolved at startup, not dynamic LLM-visible discovery.</summary>
public interface ICapabilityRegistry
{
    IReadOnlyList<string> ResolveTools(string capabilityId);
}

public record Capability(string CapabilityId, string Description, string[] ToolIds);

/// <summary>Web search abstraction — implemented in the WebSearch plugin (MVP2).</summary>
public interface IWebSearchProvider
{
    string ProviderId { get; }
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct);
}

public record SearchResult(string Title, string Url, string Snippet);
