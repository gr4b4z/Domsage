using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Plugins.Business;

/// <summary>
/// Business domain delivered entirely as plugins — no core change. Proves the extension model:
/// channels, tools, intent handlers, and a group type registered against the SDK contracts only.
/// </summary>
public sealed class WorkspacePluginRegistration : IPluginRegistration
{
    public string Namespace => "workspace";
    public string? DbSchema => null;

    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.Configure<BusinessOptions>(config);

        // Channel
        services.AddHttpClient<TeamsChannelPlugin>();
        services.AddSingleton<IChannelPlugin>(sp => sp.GetRequiredService<TeamsChannelPlugin>());

        // Group type
        services.AddSingleton<IGroupTypeProvider, WorkspaceGroupTypeProvider>();

        // Diagnostic backend + tools
        services.AddHttpClient<HttpDiagnosticsBackend>();
        services.AddSingleton<IDiagnosticsBackend>(sp => sp.GetRequiredService<HttpDiagnosticsBackend>());
        services.AddScoped<ITool, FetchPipelineLogsTool>();
        services.AddScoped<ITool, FetchMetricsTool>();
        services.AddScoped<ITool, AnnotateFailureTool>();
        services.AddHttpClient<JiraCreateIssueTool>();
        services.AddScoped<ITool>(sp => sp.GetRequiredService<JiraCreateIssueTool>());

        // Handlers
        services.AddSingleton<IIntentHandler, IncidentTriageHandler>();
        services.AddSingleton<IIntentHandler, DeploymentApprovalHandler>();
    }
}
