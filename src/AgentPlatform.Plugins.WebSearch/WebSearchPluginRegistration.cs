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
        // Web search is opt-in. When disabled (default), the web.answer_question intent isn't registered,
        // so general-knowledge questions fall through to the conversational responder (answered from the
        // model's own knowledge) instead of requiring a search backend. Enable with Plugins:WebSearch:Enabled=true.
        if (!config.GetValue("Enabled", false))
            return;

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
