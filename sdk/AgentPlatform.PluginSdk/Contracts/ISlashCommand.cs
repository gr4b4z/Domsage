using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.PluginSdk.Contracts;

/// <summary>
/// A deterministic chat command (e.g. <c>/connect-email jan@x.pl</c>). The pipeline dispatches a leading
/// "/command" to the matching handler BEFORE the LLM router — no model call, fully predictable. Plugins
/// own their commands; the host discovers them and provides a built-in <c>/help</c> listing.
/// Used for meta/config actions (linking accounts) where reliability and discoverability beat fuzzy phrasing.
/// </summary>
public interface ISlashCommand
{
    /// <summary>Command name without the leading slash, lowercase, e.g. "connect-email".</summary>
    string Name { get; }

    /// <summary>One-line description shown by <c>/help</c>.</summary>
    string Description { get; }

    /// <summary>Handle the command. <paramref name="args"/> is everything after the command name (may be empty).</summary>
    Task<string> HandleAsync(string args, ExecutionContext ctx, CancellationToken ct);
}
