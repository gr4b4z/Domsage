using System.Text.Json;
using AgentPlatform.Core.Pipeline;
using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Plugins.Skills;

/// <summary>
/// Discovers folder-based skills at startup and registers each as an IIntentHandler + prompt — the
/// runtime, no-code extension path (vs compiled plugins). Default location: ~/.agentplatform/skills/.
/// </summary>
public static class SkillsExtensions
{
    /// <summary>Reserved namespace for every skill intent (<c>skill.&lt;id&gt;</c>).</summary>
    public const string Namespace = "skill";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static IServiceCollection AddSkills(this IServiceCollection services, IConfiguration config)
    {
        var dir = config["Skills:Path"];
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agentplatform", "skills");

        var skills = Load(dir);
        var catalog = new SkillCatalog(skills);
        services.AddSingleton(catalog);
        foreach (var s in skills)
            services.AddSingleton<IIntentHandler>(new DeclarativeIntentHandler(s));

        // Decorate the prompt store so a skill's prompt resolves by its template id (file store handles the rest).
        services.AddSingleton<FileSystemPromptTemplateStore>();
        services.AddSingleton<IPromptTemplateStore>(sp =>
            new SkillPromptTemplateStore(sp.GetRequiredService<FileSystemPromptTemplateStore>(), catalog));

        return services;
    }

    /// <summary>Parses every <c>&lt;dir&gt;/&lt;name&gt;/{skill.json,prompt.txt}</c>. Malformed skills are skipped, not fatal.</summary>
    public static IReadOnlyList<LoadedSkill> Load(string dir)
    {
        var result = new List<LoadedSkill>();
        if (!Directory.Exists(dir)) return result;

        foreach (var folder in Directory.EnumerateDirectories(dir))
        {
            try
            {
                var manifestPath = Path.Combine(folder, "skill.json");
                var promptPath = Path.Combine(folder, "prompt.txt");
                if (!File.Exists(manifestPath) || !File.Exists(promptPath)) continue;

                var manifest = JsonSerializer.Deserialize<SkillManifest>(File.ReadAllText(manifestPath), JsonOpts);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id)) continue;

                var prompt = ParsePrompt(File.ReadAllText(promptPath));
                result.Add(new LoadedSkill(
                    $"{Namespace}.{manifest.Id}", $"{Namespace}_{manifest.Id}", manifest, prompt));
            }
            catch
            {
                // A single bad skill must never take down startup; skip it.
            }
        }
        return result;
    }

    // Mirrors FileSystemPromptTemplateStore: sections split on a line "---", labels stripped.
    private static PromptTemplate ParsePrompt(string text)
    {
        var idx = text.Replace("\r\n", "\n").IndexOf("\n---\n", StringComparison.Ordinal);
        var normalized = text.Replace("\r\n", "\n");
        if (idx > 0)
        {
            var sys = normalized[..idx].Replace("SYSTEM", "").Trim();
            var usr = normalized[(idx + 5)..].Replace("USER", "").Trim();
            return new PromptTemplate(sys, usr);
        }
        return new PromptTemplate(normalized.Replace("SYSTEM", "").Trim(), "<user_message>{{user_text}}</user_message>");
    }
}
