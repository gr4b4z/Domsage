using System.Text.Json;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;

namespace AgentPlatform.Eval;

public record GoldenCase(string Input, string ExpectedIntent, double MinConfidence);

public record EvalResult(int Total, int Correct, double Accuracy, List<string> Failures);

/// <summary>
/// Runs the intent-routing golden set against an ILlmProvider using the same routing prompt
/// shape as the production IntentRouter, and computes accuracy.
/// </summary>
public sealed class EvalRunner(ILlmProvider router, string[] knownIntents)
{
    public static List<GoldenCase> LoadGoldenSet(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var cases = new List<GoldenCase>();
        foreach (var e in doc.RootElement.EnumerateArray())
            cases.Add(new GoldenCase(
                e.GetProperty("input").GetString()!,
                e.GetProperty("expectedIntent").GetString()!,
                e.TryGetProperty("minConfidence", out var c) ? c.GetDouble() : 0.5));
        return cases;
    }

    public async Task<EvalResult> RunAsync(IReadOnlyList<GoldenCase> cases, CancellationToken ct)
    {
        int correct = 0;
        var failures = new List<string>();

        foreach (var c in cases)
        {
            var system =
                "You are an intent router for a household assistant. Classify into one of:\n" +
                string.Join("\n", knownIntents.Select(i => "- " + i)) + "\n" +
                "Return ONLY JSON: {\"intents\":[{\"intentId\":\"...\",\"confidence\":0.0-1.0}]}.";
            var req = new LlmRequest("gpt-4o-mini", ModelTier.Small, 0.0, null, 200, null,
                [new("system", system), new("user", $"<user_message>{c.Input}</user_message>")], null);

            var result = await router.CompleteAsync(req, ct);
            var (intent, conf) = Parse(result.Content);

            if (intent == c.ExpectedIntent && conf >= c.MinConfidence) correct++;
            else failures.Add($"'{c.Input}' → got {intent} ({conf:F2}), expected {c.ExpectedIntent} (>= {c.MinConfidence})");
        }

        return new EvalResult(cases.Count, correct, cases.Count == 0 ? 0 : (double)correct / cases.Count, failures);
    }

    private static (string intent, double conf) Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return ("none", 0);
        try
        {
            var s = content.IndexOf('{'); var e = content.LastIndexOf('}');
            using var doc = JsonDocument.Parse(content[s..(e + 1)]);
            var first = doc.RootElement.GetProperty("intents")[0];
            return (first.GetProperty("intentId").GetString() ?? "none",
                first.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0);
        }
        catch { return ("none", 0); }
    }
}
