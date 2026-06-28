namespace AgentPlatform.PluginSdk.Contracts;

/// <summary>
/// A recurring job a plugin wants the host to run on a schedule. The host discovers all registered
/// IScheduledJob services and wires them into the scheduler generically — no host knowledge of the
/// plugin. Everything (logic + schedule) ships in the plugin DLL.
/// </summary>
public interface IScheduledJob
{
    /// <summary>Stable, namespaced id, e.g. "family.reminder-scan". Also the recurring-job id.</summary>
    string JobId { get; }
    /// <summary>Cron expression (host scheduler dialect), e.g. "0 * * * *".</summary>
    string Cron { get; }
    Task RunAsync(CancellationToken ct);
}
