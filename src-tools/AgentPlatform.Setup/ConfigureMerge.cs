using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentPlatform.Setup;

/// <summary>
/// Pure read-overwrite merge of plugin field values into <c>config.json</c>. Reads the existing file,
/// updates only the targeted <c>Plugins.&lt;section&gt;</c> entries, and writes the whole document back —
/// every other section is preserved byte-for-value. Empty input is treated as "keep existing".
/// </summary>
public static class ConfigureMerge
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Returns the existing values under <c>Plugins</c>, flattened as <c>"Section:Key" → value</c>.
    /// Missing file or missing <c>Plugins</c> section → empty dictionary.
    /// </summary>
    public static Dictionary<string, string> Read(string configPath)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(configPath))
            return result;

        var root = JsonNode.Parse(File.ReadAllText(configPath));
        if (root?["Plugins"] is not JsonObject plugins)
            return result;

        foreach (var (section, sectionNode) in plugins)
        {
            if (sectionNode is not JsonObject fields) continue;
            foreach (var (key, valueNode) in fields)
            {
                if (valueNode is null) continue;
                result[$"{section}:{key}"] = valueNode.ToString();
            }
        }
        return result;
    }

    /// <summary>
    /// Writes <paramref name="fieldValues"/> into <c>Plugins.&lt;pluginSection&gt;</c> of the config at
    /// <paramref name="configPath"/>. Entries with empty/whitespace values are skipped so an existing
    /// value is preserved (Enter on a wizard prompt). All other config sections are left untouched.
    /// </summary>
    public static void Apply(string configPath, string pluginSection, IReadOnlyDictionary<string, string> fieldValues)
    {
        var root = File.Exists(configPath)
            ? JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();

        if (root["Plugins"] is not JsonObject plugins)
        {
            plugins = new JsonObject();
            root["Plugins"] = plugins;
        }

        if (plugins[pluginSection] is not JsonObject section)
        {
            section = new JsonObject();
            plugins[pluginSection] = section;
        }

        foreach (var (key, value) in fieldValues)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue; // keep existing value
            section[key] = value;
        }

        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(configPath, root.ToJsonString(WriteOptions));
    }
}
