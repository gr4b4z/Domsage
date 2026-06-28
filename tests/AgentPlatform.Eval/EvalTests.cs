using AgentPlatform.Infrastructure.Llm;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentPlatform.Eval;

public class EvalTests
{
    private const double AccuracyThreshold = 0.90;

    private static readonly string[] KnownIntents =
    [
        "family.mark_payment_paid", "family.add_payment", "family.add_task", "family.mark_task_done"
    ];

    [Fact]
    public void GoldenSet_Loads()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GoldenSets", "intent_routing.json");
        var cases = EvalRunner.LoadGoldenSet(path);
        Assert.NotEmpty(cases);
    }

    // Live eval — runs only when an OpenAI-compatible key is present. CI gate when configured.
    [SkippableFact]
    public async Task IntentRouting_MeetsAccuracyThreshold()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                  ?? Environment.GetEnvironmentVariable("Llm__ApiKey");
        Skip.If(string.IsNullOrEmpty(apiKey), "No LLM API key configured — skipping live eval.");

        var http = new HttpClient();
        var cfg = new LlmProviderConfig("openai", ModelTier.Small, "gpt-4o-mini",
            "https://api.openai.com/v1", apiKey, new PriceCard(0.15m, 0.6m, 0.075m));
        var provider = new OpenAiLlmProvider(http, cfg, NullLogger<OpenAiLlmProvider>.Instance);

        var runner = new EvalRunner(provider, KnownIntents);
        var path = Path.Combine(AppContext.BaseDirectory, "GoldenSets", "intent_routing.json");
        var cases = EvalRunner.LoadGoldenSet(path);

        var result = await runner.RunAsync(cases, CancellationToken.None);
        Assert.True(result.Accuracy >= AccuracyThreshold,
            $"Routing accuracy {result.Accuracy:P0} below {AccuracyThreshold:P0}:\n" +
            string.Join("\n", result.Failures));
    }
}
