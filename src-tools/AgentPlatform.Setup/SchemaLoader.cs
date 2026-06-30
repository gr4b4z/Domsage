using System.Text.Json;

namespace AgentPlatform.Setup;

/// <summary>
/// Discovers plugin config schemas by globbing <c>*.config-schema.json</c> in a folder. A malformed
/// or incomplete file is skipped so one bad plugin never blocks the others from appearing in the menu.
/// </summary>
public static class SchemaLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Scans <paramref name="pluginsFolder"/> for schema files. Missing folder → empty list.</summary>
    public static IReadOnlyList<PluginConfigSchemaFile> Scan(string pluginsFolder)
    {
        if (!Directory.Exists(pluginsFolder))
            return Array.Empty<PluginConfigSchemaFile>();

        var result = new List<PluginConfigSchemaFile>();
        foreach (var path in Directory.EnumerateFiles(pluginsFolder, "*.config-schema.json", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var schema = TryLoad(path);
            if (schema is not null)
                result.Add(schema);
        }
        return result;
    }

    /// <summary>Loads a single schema file. Returns <c>null</c> on any malformed/incomplete input.</summary>
    public static PluginConfigSchemaFile? TryLoad(string path)
    {
        try
        {
            var schema = JsonSerializer.Deserialize<PluginConfigSchemaFile>(File.ReadAllText(path), JsonOptions);
            if (schema is null
                || string.IsNullOrWhiteSpace(schema.PluginId)
                || string.IsNullOrWhiteSpace(schema.ConfigSection)
                || schema.Fields is null)
                return null;

            // A field without a key is unusable — treat the whole file as malformed.
            if (schema.Fields.Any(f => f is null || string.IsNullOrWhiteSpace(f.Key)))
                return null;

            return schema;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            return null;
        }
    }
}
