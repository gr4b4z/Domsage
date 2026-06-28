using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Llm;

public static class LlmRegistration
{
    /// <summary>
    /// Registers keyed ILlmProvider per ModelTier. Reads Llm config:
    /// Llm:Endpoint, Llm:ApiKey, Llm:ProviderId, Llm:Models:{Small|Medium|Large}.
    /// Falls back to OpenAI defaults.
    /// </summary>
    public static IServiceCollection AddLlmProviders(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient("llm");

        var llm = config.GetSection("Llm");
        var endpoint = llm["Endpoint"] ?? "https://api.openai.com/v1";
        var apiKey = llm["ApiKey"];
        var providerId = llm["ProviderId"] ?? "openai";

        var models = new Dictionary<ModelTier, string>
        {
            [ModelTier.Small] = llm["Models:Small"] ?? llm["Providers:default:Deployments:Small"] ?? "gpt-4o-mini",
            [ModelTier.Medium] = llm["Models:Medium"] ?? "gpt-4o-mini",
            [ModelTier.Large] = llm["Models:Large"] ?? llm["Providers:default:Deployments:Large"] ?? "gpt-4o",
        };
        var price = new PriceCard(0.15m, 0.60m, 0.075m); // gpt-4o-mini default per 1M tokens

        foreach (var (tier, model) in models)
        {
            services.AddKeyedSingleton<ILlmProvider>(tier, (sp, _) =>
            {
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("llm");
                var log = sp.GetRequiredService<ILogger<OpenAiLlmProvider>>();
                var cfg = new LlmProviderConfig(providerId, tier, model, endpoint, apiKey, price);
                return new OpenAiLlmProvider(http, cfg, log);
            });
        }

        return services;
    }
}
