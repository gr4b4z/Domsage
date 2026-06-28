using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgentPlatform.Plugins.Family.Data;

public sealed class FamilyDbContextFactory : IDesignTimeDbContextFactory<FamilyDbContext>
{
    public FamilyDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=agentplatform;Username=app;Password=localdev";
        var options = new DbContextOptionsBuilder<FamilyDbContext>()
            .UseNpgsql(conn)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new FamilyDbContext(options);
    }
}
