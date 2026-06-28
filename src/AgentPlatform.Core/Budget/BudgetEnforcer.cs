using System.Collections.Concurrent;
using AgentPlatform.Core.Contracts;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Core.Budget;

/// <summary>
/// Hierarchical budget + circuit breaker. All levels checked; most restrictive wins.
/// Spend is counted in money (USD), enforced on actual usage.
/// </summary>
public sealed class BudgetEnforcer(IBudgetRepository repo, IOptions<BudgetOptions> options)
{
    private readonly BudgetOptions _opts = options.Value;

    // Per-request counters keyed by requestId (process-local; one node MVP).
    private static readonly ConcurrentDictionary<string, RequestCounters> _perRequest = new();

    private sealed class RequestCounters
    {
        public int Iterations;
        public int LlmCalls;
        public int ToolCalls;
        public int WebSearchCalls;
    }

    private static RequestCounters Counters(string requestId) =>
        _perRequest.GetOrAdd(requestId, _ => new RequestCounters());

    public static void Clear(string requestId) => _perRequest.TryRemove(requestId, out _);

    /// <summary>Called by Validator before execution. Throws if any level is tripped.</summary>
    public async Task CheckRequestAsync(string requestId, string? groupId, CancellationToken ct)
    {
        await CheckScopeTrippedAsync("global", ct);
        if (groupId is not null)
        {
            await CheckScopeTrippedAsync($"group:{groupId}:daily", ct);
            await CheckScopeTrippedAsync($"group:{groupId}:monthly", ct);
        }

        var c = Counters(requestId);
        if (c.ToolCalls >= _opts.PerRequestMaxToolCalls)
            throw new BudgetExceededException(
                $"Per-request tool call limit ({_opts.PerRequestMaxToolCalls}) reached.");
    }

    /// <summary>Called by Planner on each ToolCalling loop iteration.</summary>
    public Task CheckIterationAsync(string requestId, CancellationToken ct)
    {
        var c = Counters(requestId);
        var n = Interlocked.Increment(ref c.Iterations);
        if (n > _opts.PerRequestMaxIterations)
            throw new BudgetExceededException(
                $"Re-planning iteration limit ({_opts.PerRequestMaxIterations}) reached.");
        return Task.CompletedTask;
    }

    public Task CheckLlmCallAsync(string requestId, CancellationToken ct)
    {
        var c = Counters(requestId);
        var n = Interlocked.Increment(ref c.LlmCalls);
        if (n > _opts.PerRequestMaxLlmCalls)
            throw new BudgetExceededException(
                $"Per-request LLM call limit ({_opts.PerRequestMaxLlmCalls}) reached.");
        return Task.CompletedTask;
    }

    public Task CountToolCallAsync(string requestId, CancellationToken ct)
    {
        Interlocked.Increment(ref Counters(requestId).ToolCalls);
        return Task.CompletedTask;
    }

    /// <summary>Per-request limit on a specific tool type (e.g. web.search).</summary>
    public Task CheckToolCallAsync(string toolId, string requestId, CancellationToken ct)
    {
        if (toolId != "web.search") return Task.CompletedTask;
        var c = Counters(requestId);
        var n = Interlocked.Increment(ref c.WebSearchCalls);
        if (n > _opts.MaxWebSearchCallsPerRequest)
            throw new BudgetExceededException(
                $"Web search limit ({_opts.MaxWebSearchCallsPerRequest}) reached for this request.");
        return Task.CompletedTask;
    }

    /// <summary>Called by UsageMeter after every LLM call.</summary>
    public async Task RecordSpendAsync(string? groupId, decimal cost, CancellationToken ct)
    {
        await repo.RecordSpendAsync("global", cost, _opts.GlobalKillSwitchUsd, ct);
        if (groupId is not null)
        {
            await repo.RecordSpendAsync($"group:{groupId}:daily", cost, _opts.PerHouseholdDailyCapUsd, ct);
            await repo.RecordSpendAsync($"group:{groupId}:monthly", cost, _opts.PerHouseholdMonthlyCapUsd, ct);
        }
    }

    public Task ResetAsync(string scopeKey, CancellationToken ct) => repo.ResetAsync(scopeKey, ct);

    private async Task CheckScopeTrippedAsync(string scopeKey, CancellationToken ct)
    {
        var state = await repo.GetAsync(scopeKey, ct);
        if (state is { Tripped: true })
            throw new BudgetExceededException($"Budget circuit breaker tripped for '{scopeKey}'.");
    }
}
