using AgentPlatform.Core.Contracts;
using AgentPlatform.Infrastructure.Blob;
using AgentPlatform.Infrastructure.Meter;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Infrastructure.Repositories;
using AgentPlatform.Infrastructure.Scheduler;
using AgentPlatform.Infrastructure.Secrets;
using AgentPlatform.PluginSdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AgentPlatform.Infrastructure.DI;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddAgentPlatformInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        var connStr = config.GetConnectionString("Postgres")
            ?? "Host=localhost;Database=agentplatform;Username=app;Password=localdev";

        var dataSource = new NpgsqlDataSourceBuilder(connStr).UseNodaTime().Build();
        services.AddSingleton(dataSource);

        services.AddSingleton<RlsConnectionInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) =>
            o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
             .UseSnakeCaseNamingConvention()
             .AddInterceptors(sp.GetRequiredService<RlsConnectionInterceptor>()),
            ServiceLifetime.Scoped);

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<IPendingIntentRepository, PendingIntentRepository>();
        services.AddScoped<IPendingConfirmationRepository, PendingConfirmationRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<IDeadLetterRepository, DeadLetterRepository>();
        services.AddScoped<IPromptVersionRepository, PromptVersionRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMemoryFactsRepository, MemoryFactsRepository>();

        services.AddScoped<IUsageMeter, PostgresUsageMeter>();
        services.AddSingleton<IBlobStorage, LocalBlobStorage>();
        services.AddSingleton<ISecretStore, EnvironmentSecretStore>();

        services.AddScoped<ISchedulerService, HangfireSchedulerService>();
        services.AddScoped<ReminderDispatcher>();

        // Generic automation engine — a recurring scheduled job, discovered like any other.
        services.AddScoped<Automation.AutomationRunner>();
        services.AddScoped<IScheduledJob, Automation.AutomationRunner>();

        services.AddSingleton<ISseHub, Notifications.InMemorySseHub>();
        services.AddScoped<IGroupDirectory, Notifications.GroupDirectory>();
        services.AddScoped<INotificationService, Notifications.NotificationService>();

        return services;
    }
}
