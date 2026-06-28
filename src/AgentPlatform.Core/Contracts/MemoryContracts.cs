namespace AgentPlatform.Core.Contracts;

public record MemoryFact(string Key, string Value, string Category, string Scope);

public interface IMemoryFactsRepository
{
    Task<IReadOnlyList<MemoryFact>> GetForUserAsync(string userId, string? groupId, CancellationToken ct);
    Task UpsertAsync(string? userId, string? groupId, string scope, string category,
        string key, string value, string source, CancellationToken ct);
}
