using System.Reflection;
using System.Runtime.Loader;

namespace AgentPlatform.Core.Plugins;

public sealed class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginAssemblyLoadContext(string pluginDirectory, string assemblyFile)
        : base(name: Path.GetFileNameWithoutExtension(assemblyFile), isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(Path.Combine(pluginDirectory, assemblyFile));
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // Shared contracts always resolve from the host so types unify.
        if (name.Name?.StartsWith("AgentPlatform.PluginSdk") == true) return null;
        var path = _resolver.ResolveAssemblyToPath(name);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}

public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string SdkVersion,
    string AssemblyFile,
    string? Description = null,
    string? Author = null);
