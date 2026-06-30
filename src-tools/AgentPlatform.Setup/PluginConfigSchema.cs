namespace AgentPlatform.Setup;

/// <summary>
/// A plugin's config schema, read from a <c>&lt;plugin&gt;.config-schema.json</c> file placed alongside
/// the plugin assembly. The CLI reads it at invocation time; the plugin code is never loaded.
/// </summary>
/// <param name="PluginId">Matches the plugin namespace prefix, e.g. <c>"email"</c>.</param>
/// <param name="DisplayName">Shown in the wizard header.</param>
/// <param name="ConfigSection">
/// The plugin's section name inside <c>Plugins</c> in config.json, e.g. <c>"Email"</c>.
/// The CLI builds the full key as <c>$"{ConfigSection}:{field.Key}"</c> → <c>"Email:ImapHost"</c>.
/// </param>
/// <param name="Fields">Ordered flat list of prompts. No grouping.</param>
public record PluginConfigSchemaFile(
    string PluginId,
    string DisplayName,
    string ConfigSection,
    ConfigField[] Fields);

/// <summary>A single prompt in the wizard.</summary>
/// <param name="Key">C# property name on the plugin's options class, e.g. <c>"ImapHost"</c>.</param>
/// <param name="Label">Displayed as the prompt text.</param>
/// <param name="Hint">Default shown in the prompt; overridden by the current config value if present.</param>
/// <param name="IsSecret"><c>true</c> → input masked, value not echoed.</param>
/// <param name="Required"><c>true</c> → empty input rejected (unless a current value exists).</param>
/// <param name="Validate">Named validation hook run after entry. Failure is non-blocking.</param>
public record ConfigField(
    string Key,
    string Label,
    string? Hint,
    bool IsSecret,
    bool Required,
    string? Validate);
