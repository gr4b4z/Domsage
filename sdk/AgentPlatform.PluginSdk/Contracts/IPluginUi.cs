using System.Reflection;

namespace AgentPlatform.PluginSdk.Contracts;

/// <summary>
/// A plugin that ships its own web UI. The host serves the plugin assembly's embedded
/// <c>wwwroot/**</c> at <c>/plugins/{PluginId}/...</c> and lists the entry in /api/plugins/ui
/// so the web shell can offer a launch tile. Everything ships in the DLL — drop it in and it works.
/// Register the implementation in DI as <c>IPluginUi</c>.
/// </summary>
public interface IPluginUi
{
    /// <summary>Stable id; also the URL segment under /plugins/.</summary>
    string PluginId { get; }
    string Title { get; }
    string Icon { get; }
    /// <summary>Entry path relative to the plugin's wwwroot, e.g. "shopping/index.html".</summary>
    string EntryPath { get; }
    /// <summary>Assembly holding the embedded wwwroot (usually the plugin's own assembly).</summary>
    Assembly AssetAssembly { get; }
}
