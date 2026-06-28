using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Plugins.Family.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace AgentPlatform.Integration.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("agentplatform")
        .WithUsername("app")
        .WithPassword("localdev")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var coreOpts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention().Options;
        await using (var core = new AppDbContext(coreOpts))
        {
            await core.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS family");
            await core.Database.MigrateAsync();
        }

        var famOpts = new DbContextOptionsBuilder<FamilyDbContext>()
            .UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention().Options;
        await using (var fam = new FamilyDbContext(famOpts))
            await fam.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
