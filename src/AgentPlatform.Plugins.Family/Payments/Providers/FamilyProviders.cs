using AgentPlatform.Plugins.Family.Data;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Plugins.Family.Payments.Providers;

public sealed class TodayPaymentsProvider(IPaymentsRepository repo) : IContextProvider
{
    public string ProviderId => "today.payments";
    public ContextScope Scope => ContextScope.Group;

    public async Task<ContextSlice> FetchAsync(ContextRequest req, CancellationToken ct)
    {
        var groupId = Guid.Parse(req.ExecutionContext.GroupId);
        var due = await repo.ListDueAsync(groupId, ct);
        return new ContextSlice(ProviderId, Scope, new
        {
            payments = due.Select(p => new
            {
                id = p.Id, creditor = p.Creditor, amount = p.Amount,
                currency = p.Currency, dueDate = p.DueDate.ToString("yyyy-MM-dd")
            })
        });
    }
}

public sealed class TodayTasksProvider(ITasksRepository repo) : IContextProvider
{
    public string ProviderId => "today.tasks";
    public ContextScope Scope => ContextScope.Group;

    public async Task<ContextSlice> FetchAsync(ContextRequest req, CancellationToken ct)
    {
        var groupId = Guid.Parse(req.ExecutionContext.GroupId);
        var open = await repo.ListOpenAsync(groupId, ct);
        return new ContextSlice(ProviderId, Scope, new
        {
            tasks = open.Select(t => new
            {
                id = t.Id, title = t.Title,
                dueDate = t.DueDate?.ToString("yyyy-MM-dd")
            })
        });
    }
}
