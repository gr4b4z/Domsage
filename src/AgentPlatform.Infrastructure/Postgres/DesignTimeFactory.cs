using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgentPlatform.Infrastructure.Postgres;

/// <summary>Lets `dotnet ef` build AppDbContext without the full host.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=agentplatform;Username=app;Password=localdev";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }
}
