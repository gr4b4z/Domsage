using System.Net.Http.Json;
using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Plugins.Business;

public sealed class BusinessOptions
{
    public string? TeamsWebhookUrl { get; set; }
    public string? JiraBaseUrl { get; set; }
    public string? JiraToken { get; set; }
    public string? DevOpsBaseUrl { get; set; }
}

// ── Group type ────────────────────────────────────────────────────────────────
public sealed class WorkspaceGroupTypeProvider : IGroupTypeProvider
{
    public string GroupType => "workspace";
    public string[] KnownRoles => ["owner", "admin", "member", "viewer"];
    public MemberRole MapToCore(string role) => role switch
    {
        "owner" or "admin" => MemberRole.Admin,
        "member" => MemberRole.Member,
        _ => MemberRole.Guest
    };
}

// ── Teams channel ─────────────────────────────────────────────────────────────
public sealed class TeamsChannelPlugin(HttpClient http, IOptions<BusinessOptions> options) : IChannelPlugin
{
    private readonly BusinessOptions _o = options.Value;
    public string ChannelId => "teams";
    public ChannelCapabilities Capabilities => new(true, true, false, true);

    public Task<InputMessage> ParseAsync(RawEvent e, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(e.Body);
        var root = doc.RootElement;
        var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var from = root.TryGetProperty("from", out var f) && f.TryGetProperty("id", out var id)
            ? id.GetString() ?? "" : "";
        return Task.FromResult(new InputMessage(Guid.NewGuid().ToString(), "teams", from, null, text, null, DateTimeOffset.UtcNow));
    }

    public async Task DeliverAsync(OutputMessage m, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_o.TeamsWebhookUrl)) return;
        // Adaptive-card-ish payload; confirmation rendered as actions.
        var card = new
        {
            type = "message",
            text = m.Text,
            actions = m.ConfirmationRequired ? new[] { "✅ Approve", "❌ Reject" } : null
        };
        await http.PostAsJsonAsync(_o.TeamsWebhookUrl, card, ct);
    }
}

// ── Diagnostic backend (LLM iterates over these in ToolCalling) ────────────────
public interface IDiagnosticsBackend
{
    Task<string> FetchPipelineLogsAsync(string pipelineRunId, CancellationToken ct);
    Task<string> FetchMetricsAsync(string pipelineRunId, CancellationToken ct);
}

public sealed class HttpDiagnosticsBackend(HttpClient http, IOptions<BusinessOptions> options) : IDiagnosticsBackend
{
    private readonly BusinessOptions _o = options.Value;
    public async Task<string> FetchPipelineLogsAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_o.DevOpsBaseUrl)) return "[no logs source configured]";
        return await http.GetStringAsync($"{_o.DevOpsBaseUrl}/logs/{id}", ct);
    }
    public async Task<string> FetchMetricsAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_o.DevOpsBaseUrl)) return "[no metrics source configured]";
        return await http.GetStringAsync($"{_o.DevOpsBaseUrl}/metrics/{id}", ct);
    }
}

public sealed class FetchPipelineLogsTool(IDiagnosticsBackend backend) : ITool
{
    public string ToolId => "workspace.devops.fetch_pipeline_logs";
    public bool HasSideEffects => false;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(("pipelineRunId", new JsonSchemaBuilder().Type(SchemaValueType.String))).Required("pipelineRunId").Build();
    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var id = input.Arguments.GetProperty("pipelineRunId").GetString()!;
        var logs = await backend.FetchPipelineLogsAsync(id, ct);
        return new ToolResult(ToolResultStatus.Success, JsonSerializer.SerializeToElement(new { logs }), null);
    }
}

public sealed class FetchMetricsTool(IDiagnosticsBackend backend) : ITool
{
    public string ToolId => "workspace.devops.fetch_metrics";
    public bool HasSideEffects => false;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(("pipelineRunId", new JsonSchemaBuilder().Type(SchemaValueType.String))).Required("pipelineRunId").Build();
    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var id = input.Arguments.GetProperty("pipelineRunId").GetString()!;
        var metrics = await backend.FetchMetricsAsync(id, ct);
        return new ToolResult(ToolResultStatus.Success, JsonSerializer.SerializeToElement(new { metrics }), null);
    }
}

public sealed class AnnotateFailureTool : ITool
{
    public string ToolId => "workspace.devops.annotate_failure";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Admin);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(
            ("pipelineRunId", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("diagnosis", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Required("pipelineRunId", "diagnosis").Build();
    public Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var diagnosis = input.Arguments.GetProperty("diagnosis").GetString()!;
        return Task.FromResult(new ToolResult(ToolResultStatus.Success, null, null,
            $"📝 Annotated pipeline failure: {diagnosis}"));
    }
}

public sealed class JiraCreateIssueTool(HttpClient http, IOptions<BusinessOptions> options) : ITool
{
    private readonly BusinessOptions _o = options.Value;
    public string ToolId => "workspace.jira.create_issue";
    public bool HasSideEffects => true;
    public ScopeRequirement RequiredScope => new(ContextScope.Group, MemberRole.Member);
    public JsonSchema InputSchema => new JsonSchemaBuilder().Type(SchemaValueType.Object)
        .Properties(
            ("project", new JsonSchemaBuilder().Type(SchemaValueType.String)),
            ("summary", new JsonSchemaBuilder().Type(SchemaValueType.String)))
        .Required("summary").Build();
    public async Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct)
    {
        var summary = input.Arguments.GetProperty("summary").GetString()!;
        if (!string.IsNullOrEmpty(_o.JiraBaseUrl))
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_o.JiraBaseUrl}/rest/api/2/issue")
            { Content = JsonContent.Create(new { fields = new { summary } }) };
            if (!string.IsNullOrEmpty(_o.JiraToken)) req.Headers.Add("Authorization", $"Bearer {_o.JiraToken}");
            await http.SendAsync(req, ct);
        }
        return new ToolResult(ToolResultStatus.Success, null, null, $"🎫 Utworzono zgłoszenie Jira: {summary}");
    }
}

// ── Handlers ──────────────────────────────────────────────────────────────────
public sealed class IncidentTriageHandler : IIntentHandler
{
    public string IntentId => "workspace.incident_triage";
    public PlannerMode Mode => PlannerMode.ToolCalling;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools =>
        ["workspace.devops.fetch_pipeline_logs", "workspace.devops.fetch_metrics", "workspace.devops.annotate_failure"];
    public string PromptTemplateId => "incident_triage";
    public ModelTier PreferredTier => ModelTier.Large;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.Required;
}

public sealed class DeploymentApprovalHandler : IIntentHandler
{
    public string IntentId => "workspace.deployment_approval";
    public PlannerMode Mode => PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => ["workspace.jira.create_issue"];
    public string PromptTemplateId => "deployment_approval";
    public ModelTier PreferredTier => ModelTier.Medium;
    public ConfirmationPolicy Confirmation => ConfirmationPolicy.Required;
}
