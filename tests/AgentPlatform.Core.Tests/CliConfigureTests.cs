using System.Text.Json;
using AgentPlatform.Setup;
using Xunit;

namespace AgentPlatform.Core.Tests;

/// <summary>
/// The deterministic, LLM-free core of the guided CLI config wizard: schema discovery,
/// read-overwrite merge into config.json, and non-blocking validation hooks.
/// </summary>
public sealed class CliConfigureTests : IDisposable
{
    private readonly string _dir;

    public CliConfigureTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cli-configure-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string WriteConfig(string json)
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static JsonElement PluginsSection(string configPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        return doc.RootElement.GetProperty("Plugins").Clone();
    }

    // ── ConfigureMerge ──────────────────────────────────────────────────────

    [Fact]
    public void ConfigureMerge_Apply_WritesFieldsToPluginsSection()
    {
        // Start from a config that already has an unrelated section — it must survive.
        var path = WriteConfig("""
            { "Llm": { "Endpoint": "https://api.openai.com/v1" } }
            """);

        ConfigureMerge.Apply(path, "Email", new Dictionary<string, string>
        {
            ["ImapHost"] = "imap.gmail.com",
            ["ImapPort"] = "993",
        });

        var plugins = PluginsSection(path);
        var email = plugins.GetProperty("Email");
        Assert.Equal("imap.gmail.com", email.GetProperty("ImapHost").GetString());
        Assert.Equal("993", email.GetProperty("ImapPort").GetString());

        // Unrelated section preserved.
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("https://api.openai.com/v1",
            doc.RootElement.GetProperty("Llm").GetProperty("Endpoint").GetString());
    }

    [Fact]
    public void ConfigureMerge_Apply_PreservesExistingFieldsOnEmptyInput()
    {
        var path = WriteConfig("""
            { "Plugins": { "Email": { "ImapHost": "old.example.com", "ImapUser": "me@old.example.com" } } }
            """);

        // Enter pressed on every field → empty strings come back; existing values must be kept.
        ConfigureMerge.Apply(path, "Email", new Dictionary<string, string>
        {
            ["ImapHost"] = "",
            ["ImapUser"] = "",
        });

        var email = PluginsSection(path).GetProperty("Email");
        Assert.Equal("old.example.com", email.GetProperty("ImapHost").GetString());
        Assert.Equal("me@old.example.com", email.GetProperty("ImapUser").GetString());
    }

    // ── SchemaLoader ────────────────────────────────────────────────────────

    [Fact]
    public void SchemaLoader_Scan_SkipsMalformedFile()
    {
        File.WriteAllText(Path.Combine(_dir, "good.config-schema.json"), """
            { "pluginId": "good", "displayName": "Good", "configSection": "Good",
              "fields": [ { "key": "Host", "label": "Host", "isSecret": false, "required": true } ] }
            """);
        File.WriteAllText(Path.Combine(_dir, "bad.config-schema.json"), "{ this is not valid json ");

        var schemas = SchemaLoader.Scan(_dir);

        var schema = Assert.Single(schemas);
        Assert.Equal("good", schema.PluginId);
    }

    [Fact]
    public void SchemaLoader_Scan_FindsExternalPlugin()
    {
        // An external plugin dropped in the folder must appear without recompiling the CLI.
        File.WriteAllText(Path.Combine(_dir, "myplugin.config-schema.json"), """
            { "pluginId": "myplugin", "displayName": "My External Plugin", "configSection": "MyPlugin",
              "fields": [ { "key": "ApiKey", "label": "API Key", "isSecret": true, "required": true } ] }
            """);

        var schemas = SchemaLoader.Scan(_dir);

        var schema = Assert.Single(schemas);
        Assert.Equal("myplugin", schema.PluginId);
        Assert.Equal("MyPlugin", schema.ConfigSection);
        var field = Assert.Single(schema.Fields);
        Assert.Equal("ApiKey", field.Key);
        Assert.True(field.IsSecret);
    }

    // ── ValidationHooks ─────────────────────────────────────────────────────

    [Fact]
    public async Task ValidationHooks_InapplicableHook_ReturnsOk()
    {
        // Unknown / inapplicable hook names must never block a save.
        var (ok, error) = await ValidationHooks.RunAsync("no-such-hook", new Dictionary<string, string>());

        Assert.True(ok);
        Assert.True(string.IsNullOrEmpty(error));
    }
}
