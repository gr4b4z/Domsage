using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.PluginSdk.Contracts;

/// <summary>The single entry point contract every external plugin must implement.</summary>
public interface IPluginRegistration
{
    /// <summary>
    /// Unique namespace. All ToolIds, IntentIds and ProviderIds registered by this plugin
    /// MUST start with Namespace + ".".
    /// </summary>
    string Namespace { get; }

    /// <summary>
    /// PostgreSQL schema for this plugin's own tables. null = no DB tables.
    /// Convention: Namespace with dots stripped.
    /// </summary>
    string? DbSchema => null;

    void Register(IServiceCollection services, IConfiguration pluginConfig);
}
