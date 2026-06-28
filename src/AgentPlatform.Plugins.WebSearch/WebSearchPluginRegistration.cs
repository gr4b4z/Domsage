using AgentPlatform.PluginSdk.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Plugins.WebSearch;

public sealed class WebSearchPluginRegistration : IPluginRegistration
{
    public string Namespace => "web";
    public string? DbSchema => null;

    public void Register(IServiceCollection services, IConfiguration config)
    {
        var strategy = config["RotationStrategy"] ?? "round-robin";
        if (strategy == "random") services.AddSingleton<IKeyRotationStrategy, RandomKeyStrategy>();
        else services.AddSingleton<IKeyRotationStrategy, RoundRobinKeyStrategy>();

        var provider = config["Provider"] ?? "searxng";
        switch (provider)
        {
            case "brave":
                services.Configure<BraveOptions>(config.GetSection("Brave"));
                services.AddHttpClient<BraveSearchProvider>();
                services.AddSingleton<IWebSearchProvider, BraveSearchProvider>();
                break;
            default: // searxng
                var baseUrl = config["SearxngBaseUrl"] ?? "http://localhost:8092";
                services.AddHttpClient("searxng");
                services.AddSingleton<IWebSearchProvider>(sp =>
                    new SearXngProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient("searxng"), baseUrl));
                break;
        }

        services.AddScoped<ITool, WebSearchTool>();
        services.AddSingleton<IIntentHandler, AnswerQuestionHandler>();
    }
}
