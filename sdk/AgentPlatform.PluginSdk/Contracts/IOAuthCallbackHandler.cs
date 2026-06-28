namespace AgentPlatform.PluginSdk.Contracts;

/// <summary>
/// Handles the OAuth redirect (GET) for one provider. The host maps <c>GET /oauth/{Provider}/callback</c>
/// generically — it knows nothing about Google/Microsoft; the plugin validates state and exchanges the code.
/// Returns an HTML page shown to the user in the browser after consent.
/// </summary>
public interface IOAuthCallbackHandler
{
    /// <summary>Provider id; the callback route is <c>/oauth/{Provider}/callback</c>.</summary>
    string Provider { get; }

    Task<OAuthCallbackResult> HandleAsync(IReadOnlyDictionary<string, string> query, CancellationToken ct);
}

public sealed record OAuthCallbackResult(string Html, int StatusCode = 200);
