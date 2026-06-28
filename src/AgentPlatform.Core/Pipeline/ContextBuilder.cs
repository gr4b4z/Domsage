using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.Registry;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Core.Pipeline;

/// <summary>
/// Fetches only what the matched Intent Handler declares. Enforces ContextScope per provider —
/// a User-scoped provider may not be fetched against a Group-only context and vice versa is allowed.
/// Scoped.
/// </summary>
public sealed class ContextBuilder(PluginRegistry registry, IServiceProvider sp)
{
    public async Task<AgentContext> FetchAsync(
        ContextRequest req, IIntentHandler handler, CancellationToken ct)
    {
        var providerIds = handler.RequiredContextProviders;
        if (providerIds.Length == 0)
            return new AgentContext([], []);

        var tasks = providerIds.Select(async id =>
        {
            var provider = registry.ResolveProvider(id, sp);

            // Scope enforcement: a Group-scoped provider requires an active group.
            if (provider.Scope == ContextScope.Group && string.IsNullOrEmpty(req.ExecutionContext.GroupId))
                throw new ContextScopeViolationException(
                    $"Provider '{id}' is Group-scoped but no group is active.");

            return await provider.FetchAsync(req, ct);
        });

        var slices = await Task.WhenAll(tasks);
        return new AgentContext(slices, providerIds);
    }
}
