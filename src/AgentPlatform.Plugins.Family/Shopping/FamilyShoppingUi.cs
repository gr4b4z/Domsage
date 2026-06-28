using System.Reflection;
using AgentPlatform.PluginSdk.Contracts;

namespace AgentPlatform.Plugins.Family.Shopping;

/// <summary>Declares the shopping checklist web UI shipped inside this plugin DLL.</summary>
public sealed class FamilyShoppingUi : IPluginUi
{
    public string PluginId => "family";
    public string Title => "Lista zakupów";
    public string Icon => "🛒";
    public string EntryPath => "shopping/index.html";
    public Assembly AssetAssembly => typeof(FamilyShoppingUi).Assembly;
}
