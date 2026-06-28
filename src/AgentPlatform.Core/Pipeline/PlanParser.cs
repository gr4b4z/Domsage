using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Core.Pipeline;

/// <summary>Parses LLM JSON output into an ActionPlan. Never throws — returns a clarify plan on failure.</summary>
public sealed class PlanParser(ILogger<PlanParser> log)
{
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement;

    public ActionPlan Parse(string? content, IIntentHandler handler, ExecutionContext ctx, string promptVersion)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Clarify(handler, ctx, promptVersion);

        try
        {
            var json = ExtractJson(content);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var toolId = GetString(root, "tool") ?? GetString(root, "toolId");
            if (string.IsNullOrWhiteSpace(toolId))
                return Clarify(handler, ctx, promptVersion);

            var target = GetString(root, "target") ?? GetString(root, "targetId");
            var confidence = root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetDouble() : 0.5;
            var requiresConfirm = root.TryGetProperty("requiresConfirmation", out var rc)
                                  && rc.ValueKind == JsonValueKind.True;
            var idem = GetString(root, "idempotencyKey")
                       ?? BuildKey(ctx, target, toolId);
            // Prefer an explicit toolInput; otherwise salvage top-level args (the model sometimes
            // emits arguments alongside the control fields instead of nesting them).
            var toolInput = root.TryGetProperty("toolInput", out var ti) && ti.ValueKind == JsonValueKind.Object
                ? ti.Clone()
                : SalvageToolInput(root);

            return new ActionPlan(
                Intent: handler.IntentId,
                Mode: handler.Mode,
                Scope: ContextScope.Group,
                TargetId: target,
                ToolId: toolId!,
                Confidence: confidence,
                RequiresConfirmation: requiresConfirm,
                IdempotencyKey: idem,
                PromptVersion: promptVersion,
                ModelTier: handler.PreferredTier,
                DiagnosticSteps: 0,
                ToolInput: toolInput);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "PlanParser failed for intent {Intent}; returning clarify", handler.IntentId);
            return Clarify(handler, ctx, promptVersion);
        }
    }

    public static ActionPlan Clarify(IIntentHandler handler, ExecutionContext ctx, string promptVersion) =>
        new(
            Intent: "clarify",
            Mode: handler.Mode,
            Scope: ContextScope.User,
            TargetId: null,
            ToolId: "clarify",
            Confidence: 0.0,
            RequiresConfirmation: false,
            IdempotencyKey: $"{ctx.RequestId}:clarify",
            PromptVersion: promptVersion,
            ModelTier: handler.PreferredTier,
            DiagnosticSteps: 0,
            ToolInput: EmptyObject);

    private static readonly HashSet<string> ControlKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "tool", "toolId", "target", "targetId", "confidence", "requiresConfirmation",
        "idempotencyKey", "intent", "mode", "scope", "toolInput", "diagnosticSteps"
    };

    private static JsonElement SalvageToolInput(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return EmptyObject;
        var args = new Dictionary<string, JsonElement>();
        foreach (var prop in root.EnumerateObject())
            if (!ControlKeys.Contains(prop.Name))
                args[prop.Name] = prop.Value.Clone();
        return args.Count == 0 ? EmptyObject : JsonSerializer.SerializeToElement(args);
    }

    private static string BuildKey(ExecutionContext ctx, string? target, string toolId) =>
        $"{ctx.GroupId}:{target ?? ctx.RequestId}:{toolId}";

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string ExtractJson(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : content;
    }
}
