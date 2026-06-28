using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Plugins.Family.Data;
using AgentPlatform.PluginSdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AgentPlatform.Plugins.Family.DI;

public static class FamilyPluginsExtensions
{
    public static IServiceCollection AddFamilyPlugins(this IServiceCollection services, IConfiguration config)
    {
        // Tools + providers — Scoped (need scoped DbContext/repos).
        services.Scan(scan => scan
            .FromAssemblyOf<FamilyPluginsMarker>()
            .AddClasses(c => c.AssignableTo<ITool>()).AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo<IContextProvider>()).AsImplementedInterfaces().WithScopedLifetime());

        // Handlers — stateless declarations, Singleton.
        services.Scan(scan => scan
            .FromAssemblyOf<FamilyPluginsMarker>()
            .AddClasses(c => c.AssignableTo<IIntentHandler>()).AsImplementedInterfaces().WithSingletonLifetime());

        // Group type provider + hooks.
        services.AddSingleton<IGroupTypeProvider, HouseholdGroupTypeProvider>();

        // Web UI shipped inside this plugin (served by host at /plugins/family/...).
        services.AddSingleton<IPluginUi, Shopping.FamilyShoppingUi>();

        // FamilyDbContext — own schema, shares connection string + RLS interceptor.
        services.AddDbContext<FamilyDbContext>((sp, o) =>
            o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
             .UseSnakeCaseNamingConvention()
             .AddInterceptors(sp.GetRequiredService<RlsConnectionInterceptor>()),
            ServiceLifetime.Scoped);
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FamilyDbContext>());

        services.AddScoped<IPaymentsRepository, PaymentsRepository>();
        services.AddScoped<ITasksRepository, TasksRepository>();
        services.AddScoped<IShoppingRepository, ShoppingRepository>();
        services.AddScoped<IRenewalsRepository, RenewalsRepository>();
        services.AddScoped<IChoresRepository, ChoresRepository>();
        services.AddScoped<IInvoiceDocumentRepository, InvoiceDocumentRepository>();

        return services;
    }
}
