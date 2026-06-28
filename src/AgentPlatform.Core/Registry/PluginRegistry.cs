using AgentPlatform.Core.Contracts;
using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Core.Registry;

/// <summary>
/// Singleton. Resolves Scoped plugins (tools, providers) via IServiceProvider at call time;
/// Singleton plugins (handlers, channels) injected directly.
/// </summary>
public sealed class PluginRegistry
{
    private readonly IReadOnlyDictionary<string, IIntentHandler> _handlers;
    private readonly IReadOnlyDictionary<string, IChannelPlugin> _channels;
    private readonly ILogger<PluginRegistry> _log;

    /// <summary>Known plugin namespaces, set at startup before ValidateContracts.</summary>
    public HashSet<string> Namespaces { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PluginRegistry(
        IEnumerable<IIntentHandler> handlers,
        IEnumerable<IChannelPlugin> channels,
        ILogger<PluginRegistry> log)
    {
        _handlers = handlers.GroupBy(h => h.IntentId).ToDictionary(g => g.Key, g => g.First());
        _channels = channels.GroupBy(c => c.ChannelId).ToDictionary(g => g.Key, g => g.First());
        _log = log;
    }

    public ITool ResolveTool(string toolId, IServiceProvider scopedSp) =>
        scopedSp.GetServices<ITool>().Single(t => t.ToolId == toolId);

    public IContextProvider ResolveProvider(string providerId, IServiceProvider scopedSp) =>
        scopedSp.GetServices<IContextProvider>().Single(p => p.ProviderId == providerId);

    public IIntentHandler GetHandler(string intentId) => _handlers[intentId];
    public bool TryGetHandler(string intentId, out IIntentHandler? handler) =>
        _handlers.TryGetValue(intentId, out handler);
    public IChannelPlugin GetChannel(string channelId) => _channels[channelId];
    public IEnumerable<IIntentHandler> AllHandlers => _handlers.Values;

    /// <summary>Called once at startup after all plugins loaded. Throws on violations.</summary>
    public void ValidateContracts(IServiceProvider sp)
    {
        var errors = new List<string>();
        var tools = sp.GetServices<ITool>().ToList();
        var providers = sp.GetServices<IContextProvider>().ToList();

        foreach (var dup in tools.GroupBy(t => t.ToolId).Where(g => g.Count() > 1))
            errors.Add($"Duplicate ToolId: '{dup.Key}'");
        foreach (var dup in providers.GroupBy(p => p.ProviderId).Where(g => g.Count() > 1))
            errors.Add($"Duplicate ProviderId: '{dup.Key}'");

        foreach (var tool in tools)
            if (!IsBuiltinId(tool.ToolId) && !Namespaces.Any(n => tool.ToolId.StartsWith(n + ".")))
                errors.Add($"Tool '{tool.ToolId}' has no matching plugin namespace. " +
                           $"Known: [{string.Join(", ", Namespaces)}]");

        foreach (var handler in _handlers.Values)
            if (!IsBuiltinId(handler.IntentId) && !Namespaces.Any(n => handler.IntentId.StartsWith(n + ".")))
                errors.Add($"Handler '{handler.IntentId}' has no matching plugin namespace.");

        var toolIds = tools.Select(t => t.ToolId).ToHashSet();
        foreach (var handler in _handlers.Values)
            foreach (var toolId in handler.AllowedTools)
                if (!toolIds.Contains(toolId))
                    _log.LogWarning("Handler {H} declares AllowedTool '{T}' not registered",
                        handler.IntentId, toolId);

        if (errors.Count > 0)
            throw new PluginConflictException(
                "Plugin contract violations:\n" + string.Join("\n", errors.Select(e => "  • " + e)));
    }

    private static bool IsBuiltinId(string id) =>
        id.StartsWith("system.") || id.StartsWith("conversation.") || id.StartsWith("user.")
        || id == "clarify" || id == "fallback";
}
