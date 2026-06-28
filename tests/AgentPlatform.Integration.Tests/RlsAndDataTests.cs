using Npgsql;
using Xunit;

namespace AgentPlatform.Integration.Tests;

[Collection("postgres")]
public class RlsAndDataTests(PostgresFixture fx)
{
    [Fact]
    public async Task Rls_IsolatesPaymentsByGroup()
    {
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(fx.ConnectionString);
        await conn.OpenAsync();

        // Seed two groups (public tables, no RLS).
        await Exec(conn, "INSERT INTO groups (id, type, name, created_at) VALUES (@id,'household','A', NOW())", ("id", groupA));
        await Exec(conn, "INSERT INTO groups (id, type, name, created_at) VALUES (@id,'household','B', NOW())", ("id", groupB));

        // RLS is bypassed by superusers/table owners — production must run the app as a
        // non-superuser role. Create one and SET ROLE to it so the policy actually enforces.
        await Exec(conn, "DROP ROLE IF EXISTS app_rls");
        await Exec(conn, "CREATE ROLE app_rls NOSUPERUSER");
        await Exec(conn, "GRANT USAGE ON SCHEMA family TO app_rls");
        await Exec(conn, "GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA family TO app_rls");
        await Exec(conn, "SET ROLE app_rls");

        // Insert a payment into each group — set the RLS var to match (WITH CHECK enforced under FORCE).
        await SetGroup(conn, groupA);
        await Exec(conn, "INSERT INTO family.payments (id, group_id, creditor, amount, currency, due_date, status, source, created_at) " +
            "VALUES (gen_random_uuid(), @g, 'PGNiG', 100, 'PLN', CURRENT_DATE, 'pending', 'manual', NOW())", ("g", groupA));

        await SetGroup(conn, groupB);
        await Exec(conn, "INSERT INTO family.payments (id, group_id, creditor, amount, currency, due_date, status, source, created_at) " +
            "VALUES (gen_random_uuid(), @g, 'PKN', 200, 'PLN', CURRENT_DATE, 'pending', 'manual', NOW())", ("g", groupB));

        // As group A, only A's payment is visible.
        await SetGroup(conn, groupA);
        var countA = await Scalar(conn, "SELECT COUNT(*) FROM family.payments");
        Assert.Equal(1L, countA);
        var creditorA = await ScalarText(conn, "SELECT creditor FROM family.payments LIMIT 1");
        Assert.Equal("PGNiG", creditorA);

        // As group B, only B's payment is visible.
        await SetGroup(conn, groupB);
        var countB = await Scalar(conn, "SELECT COUNT(*) FROM family.payments");
        Assert.Equal(1L, countB);
    }

    [Fact]
    public async Task Idempotency_InsertOnConflict_OnlyOnce()
    {
        await using var conn = new NpgsqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        var key = "test-idem-" + Guid.NewGuid();

        var first = await ExecRows(conn,
            "INSERT INTO idempotency_keys (key, result, created_at, expires_at) " +
            "VALUES (@k, '{}'::jsonb, NOW(), NOW() + INTERVAL '7 days') ON CONFLICT (key) DO NOTHING", ("k", key));
        var second = await ExecRows(conn,
            "INSERT INTO idempotency_keys (key, result, created_at, expires_at) " +
            "VALUES (@k, '{}'::jsonb, NOW(), NOW() + INTERVAL '7 days') ON CONFLICT (key) DO NOTHING", ("k", key));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    private static async Task SetGroup(NpgsqlConnection conn, Guid g)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SET app.current_group_id = '{g}'";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task Exec(NpgsqlConnection conn, string sql, params (string, object)[] ps)
        => await ExecRows(conn, sql, ps);

    private static async Task<int> ExecRows(NpgsqlConnection conn, string sql, params (string, object)[] ps)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> Scalar(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<string> ScalarText(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
}
