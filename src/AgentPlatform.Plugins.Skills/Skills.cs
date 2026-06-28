using System.Text.Json;
using AgentPlatform.Core.Pipeline;
using AgentPlatform.PluginSdk.Contracts;

namespace AgentPlatform.Plugins.Skills;

/// <summary>
/// A skill authored as a folder (no recompilation): <c>skill.json</c> (manifest) + <c>prompt.txt</c>.
/// It contributes an intent + prompt, but may ONLY reference tools that already exist — the trust
/// boundary, idempotency, budget and confirmation all still apply. There is no shell or arbitrary code:
/// a skill is routing + a prompt + an allow-list of vetted deterministic tools.
/// </summary>
public sealed record SkillManifest(
    string Id,
    string? Description,
    string Mode = "ContextFirst",
    string[]? AllowedTools = null,
    string Confirmation = "NotRequired",
    string Tier = "Small",
    bool PhraseResult = false);

public sealed record LoadedSkill(string IntentId, string PromptTemplateId, SkillManifest Manifest, PromptTemplate Prompt);

/// <summary>Holds the skills loaded at startup; lets the prompt-store decorator resolve their prompts.</summary>
public sealed class SkillCatalog(IEnumerable<LoadedSkill> skills)
{
    public IReadOnlyList<LoadedSkill> Skills { get; } = skills.ToList();
    private readonly Dictionary<string, PromptTemplate> _byTemplate =
        skills.ToDictionary(s => s.PromptTemplateId, s => s.Prompt);

    public bool TryGetPrompt(string templateId, out PromptTemplate? prompt)
    {
        var ok = _byTemplate.TryGetValue(templateId, out var p);
        prompt = p;
        return ok;
    }
}

/// <summary>An IIntentHandler built entirely from a skill manifest — the declarative equivalent of a coded handler.</summary>
public sealed class DeclarativeIntentHandler(LoadedSkill skill) : IIntentHandler
{
    public string IntentId => skill.IntentId;
    public PlannerMode Mode => Enum.TryParse<PlannerMode>(skill.Manifest.Mode, true, out var m) ? m : PlannerMode.ContextFirst;
    public string[] RequiredContextProviders => [];
    public string[] AllowedTools => skill.Manifest.AllowedTools ?? [];
    public string PromptTemplateId => skill.PromptTemplateId;
    public ModelTier PreferredTier => Enum.TryParse<ModelTier>(skill.Manifest.Tier, true, out var t) ? t : ModelTier.Small;
    public ConfirmationPolicy Confirmation =>
        Enum.TryParse<ConfirmationPolicy>(skill.Manifest.Confirmation, true, out var c) ? c : ConfirmationPolicy.NotRequired;
    public string? Description => skill.Manifest.Description;
    public bool PhraseResult => skill.Manifest.PhraseResult;
}

/// <summary>Serves skill prompts by their template id; delegates everything else to the file-system store.</summary>
public sealed class SkillPromptTemplateStore(FileSystemPromptTemplateStore inner, SkillCatalog catalog) : IPromptTemplateStore
{
    public PromptTemplate Get(string templateId) =>
        catalog.TryGetPrompt(templateId, out var p) && p is not null ? p : inner.Get(templateId);
}
