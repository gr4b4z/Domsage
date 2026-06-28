using AgentPlatform.Plugins.Family.Data;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Plugins.Family.Payments;

/// <summary>
/// group.bill_anomalies — flags creditors whose most recent amount deviates &gt;30%
/// from the historical average for that creditor (≥2 prior data points).
/// Pure deterministic computation; ties into a future Home Assistant / PV provider.
/// </summary>
public sealed class BillAnomalyProvider(IPaymentsRepository repo) : IContextProvider
{
    private const decimal Threshold = 0.30m;
    public string ProviderId => "group.bill_anomalies";
    public ContextScope Scope => ContextScope.Group;

    public async Task<ContextSlice> FetchAsync(ContextRequest req, CancellationToken ct)
    {
        var all = await repo.ListAllAsync(Guid.Parse(req.ExecutionContext.GroupId), ct);
        var anomalies = new List<object>();

        foreach (var grp in all.GroupBy(p => p.Creditor, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = grp.OrderByDescending(p => p.CreatedAt).ToList();
            if (ordered.Count < 3) continue;
            var latest = ordered[0];
            var history = ordered.Skip(1).ToList();
            var avg = history.Average(p => p.Amount);
            if (avg == 0) continue;
            var deviation = (latest.Amount - avg) / avg;
            if (Math.Abs(deviation) >= Threshold)
                anomalies.Add(new
                {
                    creditor = grp.Key,
                    latest = latest.Amount,
                    average = Math.Round(avg, 2),
                    deviationPct = Math.Round(deviation * 100, 1)
                });
        }

        return new ContextSlice(ProviderId, Scope, new { anomalies });
    }
}
