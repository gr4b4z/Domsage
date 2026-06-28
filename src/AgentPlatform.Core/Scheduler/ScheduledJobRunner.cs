using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Core.Scheduler;

/// <summary>
/// Generic dispatcher the host's scheduler invokes by job id. Resolves the matching IScheduledJob
/// from DI and runs it. This keeps the host's recurring-job registration free of any plugin type.
/// </summary>
public sealed class ScheduledJobRunner(IServiceProvider sp, ILogger<ScheduledJobRunner> log)
{
    public async Task RunAsync(string jobId, CancellationToken ct)
    {
        var job = sp.GetServices<IScheduledJob>().FirstOrDefault(j => j.JobId == jobId);
        if (job is null) { log.LogWarning("Scheduled job {JobId} not found", jobId); return; }
        await job.RunAsync(ct);
    }
}
