using System.Collections.Concurrent;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.PluginSdk.Testing;

/// <summary>No-op usage meter for plugin tests.</summary>
public sealed class NoOpUsageMeter : IUsageMeter
{
    public Task RecordAsync(UsageEvent e, CancellationToken ct) => Task.CompletedTask;
    public Task<SpendSnapshot> GetSpendAsync(BudgetScope scope, Window window, CancellationToken ct) =>
        Task.FromResult(new SpendSnapshot(scope, 0m, false));
}

/// <summary>In-memory blob storage for plugin tests.</summary>
public sealed class InMemoryBlobStorage : IBlobStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new();

    public async Task<string> StoreAsync(Stream data, string mediaType, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await data.CopyToAsync(ms, ct);
        var key = Guid.NewGuid().ToString("N");
        _store[key] = ms.ToArray();
        return key;
    }

    public Task<Stream> ReadAsync(string storageRef, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream(_store[storageRef]));
}

/// <summary>Builds a full Scoped DI container with the plugin registered + SDK fakes.</summary>
public static class PluginTestHarness
{
    public static IServiceProvider Build(
        IPluginRegistration registration,
        IConfiguration? pluginConfig = null,
        Action<IServiceCollection>? overrides = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUsageMeter, NoOpUsageMeter>();
        services.AddSingleton<IBlobStorage, InMemoryBlobStorage>();
        registration.Register(services, pluginConfig ?? new ConfigurationBuilder().Build());
        overrides?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static ExecutionContext FakeContext(
        string userId = "test-user-id",
        string householdId = "test-household-id",
        string groupType = "household",
        MemberRole role = MemberRole.Member,
        bool isIncognito = false) =>
        new(
            RequestId: Guid.NewGuid().ToString(),
            UserId: userId,
            GroupId: householdId,
            GroupType: groupType,
            UserRole: role,
            ChannelId: "test",
            ConversationId: Guid.NewGuid().ToString(),
            IsIncognito: isIncognito,
            StartedAt: DateTimeOffset.UtcNow);
}
