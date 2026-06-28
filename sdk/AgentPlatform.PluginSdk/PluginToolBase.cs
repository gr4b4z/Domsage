using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Json.Schema;

namespace AgentPlatform.PluginSdk;

/// <summary>
/// Optional base class for tools. Derived classes implement ExecuteAsync, InputSchema,
/// RequiredScope and HasSideEffects; ToolId defaults to "{namespace-lowered}.{type-name-lowered}"
/// unless overridden.
/// </summary>
public abstract class PluginToolBase : ITool
{
    public virtual string ToolId
    {
        get
        {
            var ns = GetType().Namespace ?? "plugin";
            var root = ns.Split('.').FirstOrDefault()?.ToLowerInvariant() ?? "plugin";
            var name = GetType().Name.Replace("Tool", "").ToLowerInvariant();
            return $"{root}.{name}";
        }
    }

    public abstract JsonSchema InputSchema { get; }
    public abstract ScopeRequirement RequiredScope { get; }
    public abstract bool HasSideEffects { get; }
    public abstract Task<ToolResult> ExecuteAsync(ToolInput input, ExecutionContext ctx, CancellationToken ct);
}
