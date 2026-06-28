using AgentPlatform.Core.Audit;
using AgentPlatform.Core.Budget;
using AgentPlatform.Core.Bus;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.Pipeline;
using AgentPlatform.Core.Registry;
using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPlatform.Core.DI;

public static class CoreServiceExtensions
{
    public static IServiceCollection AddAgentPlatformCore(
        this IServiceCollection services, IConfiguration config)
    {
        // Pipeline components — Scoped (per message, DB access + per-request state).
        services.AddScoped<IntentRouter>();
        services.AddScoped<ContextBuilder>();
        services.AddScoped<Planner>();
        services.AddScoped<PromptBuilder>();
        services.AddScoped<PlanningStrategy>();
        services.AddScoped<PlanParser>();
        services.AddScoped<PlanResponseValidator>();
        services.AddScoped<ActionValidator>();
        services.AddScoped<ToolExecutor>();
        services.AddScoped<OutputRouter>();
        services.AddScoped<BudgetEnforcer>();
        services.AddScoped<AuditLogger>();
        services.AddScoped<AgentPipeline>();
        services.AddScoped<ResponseBuilder>();
        services.AddScoped<ConversationResolver>();
        services.AddScoped<ConversationWriter>();

        // Singletons — stateless / thread-safe.
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<InProcessMessageBus>();
        services.AddSingleton<IMessageBus>(sp => sp.GetRequiredService<InProcessMessageBus>());
        services.AddSingleton<IPromptTemplateStore, FileSystemPromptTemplateStore>();
        services.AddHostedService<MessageBusConsumer>();

        // Execution context accessor — AsyncLocal-backed singleton so the RLS interceptor
        // (a singleton resolving from root) can read the context that flows with the pipeline run.
        services.AddSingleton<IExecutionContextAccessor, ExecutionContextAccessor>();

        services.Configure<BudgetOptions>(config.GetSection("Budget"));
        services.Configure<PromptOptions>(config.GetSection("Prompts"));

        return services;
    }
}

/// <summary>AsyncLocal-backed holder — the context flows with the async pipeline run (incl. DB connections).</summary>
public sealed class ExecutionContextAccessor : IExecutionContextAccessor
{
    private static readonly AsyncLocal<ExecutionContext?> _current = new();
    public ExecutionContext? Current { get => _current.Value; set => _current.Value = value; }
}
