using System.Data.Common;
using AgentPlatform.Core.Contracts;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgentPlatform.Infrastructure.Postgres;

/// <summary>
/// Sets PostgreSQL session variables on every connection open so RLS policies apply.
/// Pool-safe: uses SET (session) keyed to the current ExecutionContext. Singleton.
/// </summary>
public sealed class RlsConnectionInterceptor(IServiceProvider sp) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    {
        var accessor = (IExecutionContextAccessor?)sp.GetService(typeof(IExecutionContextAccessor));
        var execCtx = accessor?.Current;
        if (execCtx is null || string.IsNullOrEmpty(execCtx.GroupId)) return;

        if (!Guid.TryParse(execCtx.GroupId, out var gid)) return;
        Guid.TryParse(execCtx.UserId, out var uid);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            $"SET app.current_group_id = '{gid}'; SET app.current_user_id = '{uid}';";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        var accessor = (IExecutionContextAccessor?)sp.GetService(typeof(IExecutionContextAccessor));
        var execCtx = accessor?.Current;
        if (execCtx is null || string.IsNullOrEmpty(execCtx.GroupId)) return;
        if (!Guid.TryParse(execCtx.GroupId, out var gid)) return;
        Guid.TryParse(execCtx.UserId, out var uid);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SET app.current_group_id = '{gid}'; SET app.current_user_id = '{uid}';";
        cmd.ExecuteNonQuery();
    }
}
