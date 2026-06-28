using Microsoft.Extensions.Configuration;

namespace AgentPlatform.Infrastructure.Secrets;

public interface ISecretStore
{
    string? Get(string key);
}

/// <summary>Reads secrets from environment variables, falling back to configuration. Singleton.</summary>
public sealed class EnvironmentSecretStore(IConfiguration config) : ISecretStore
{
    public string? Get(string key) =>
        Environment.GetEnvironmentVariable(key.Replace(':', '_').ToUpperInvariant())
        ?? config[key];
}
