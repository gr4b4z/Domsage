using System.Text.Json;
using AgentPlatform.Core.Contracts;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Core.Pipeline;

/// <summary>
/// Builds an LlmRequest from a versioned template + context. Loads the active PromptVersion
/// from prompt_versions; falls back to a file/template + provider defaults when none is stored.
/// </summary>
public sealed class PromptBuilder(
    IPromptVersionRepository prompts,
    IPromptTemplateStore templates,
    IOptions<PromptOptions> options)
{
    private readonly PromptOptions _opts = options.Value;

    public sealed record BuiltPrompt(LlmRequest Request, string Version);

    public async Task<BuiltPrompt> BuildAsync(
        IIntentHandler handler, InputMessage msg, AgentContext ctx, CancellationToken ct)
    {
        var templateId = handler.PromptTemplateId;
        var version = await prompts.GetActiveAsync(templateId, ct);

        var (system, user, modelId, tier, temp, topP, maxTokens, reasoning, ver) =
            version is not null
                ? Resolve(version, templates.Get(templateId), handler)
                : ResolveDefault(templates.Get(templateId), handler);

        var allowedToolsJson = JsonSerializer.Serialize(handler.AllowedTools);
        var contextJson = ctx.ToPromptJson();

        system = Substitute(system, allowedToolsJson, contextJson, msg.Text);
        user = Substitute(user, allowedToolsJson, contextJson, msg.Text);

        var messages = new List<LlmMessage>
        {
            new("system", system),
            new("user", user)
        };

        return new BuiltPrompt(
            new LlmRequest(modelId, tier, temp, topP, maxTokens, reasoning, messages, Tools: null),
            ver);
    }

    private static string Substitute(string s, string tools, string context, string userText) =>
        s.Replace("{{allowed_tools_json}}", tools)
         .Replace("{{context_json}}", context)
         .Replace("{{user_text}}", userText);

    private (string, string, string, ModelTier, double, double?, int?, string?, string) Resolve(
        PromptVersionRecord v, PromptTemplate t, IIntentHandler handler) =>
        (t.System, t.User, v.ModelId, handler.PreferredTier, v.Temperature, v.TopP, v.MaxTokens,
         v.ReasoningLevel, v.Id);

    private (string, string, string, ModelTier, double, double?, int?, string?, string) ResolveDefault(
        PromptTemplate t, IIntentHandler handler)
    {
        var ver = _opts.ActiveVersions.GetValueOrDefault(handler.PromptTemplateId, "v0.0.0");
        return (t.System, t.User, "gpt-4o-mini", handler.PreferredTier, 0.2, null, null, null, ver);
    }
}

public sealed record PromptTemplate(string System, string User);

/// <summary>Loads prompt templates by id from disk (templateId__version.txt) with a built-in default.</summary>
public interface IPromptTemplateStore
{
    PromptTemplate Get(string templateId);
}
