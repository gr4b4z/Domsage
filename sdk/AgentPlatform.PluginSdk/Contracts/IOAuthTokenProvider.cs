namespace AgentPlatform.PluginSdk.Contracts;

/// <summary>
/// Supplies a valid access token for an external account a user has connected (Google, Microsoft, …),
/// refreshing transparently. One provider per external service; capability plugins (e.g. Calendar)
/// depend on this abstraction, never on a specific provider's internals.
/// </summary>
public interface IOAuthTokenProvider
{
    /// <summary>Provider id, e.g. "google" or "microsoft".</summary>
    string Provider { get; }

    Task<bool> IsConnectedAsync(string userId, CancellationToken ct);

    /// <summary>A fresh access token for the user, or null if they have not connected this provider.</summary>
    Task<string?> GetAccessTokenAsync(string userId, CancellationToken ct);
}
