using System.Text.Json;
using AgentPlatform.Infrastructure.Automation;
using Xunit;

namespace AgentPlatform.Core.Tests;

/// <summary>The deterministic heart of the automation engine — runs every period, so it must be exact.</summary>
public sealed class AutomationConditionTests
{
    // Mirrors a weather.current result: { Current: {...}, Days: [ {PrecipProb}, {PrecipProb} ] }
    private static JsonElement Weather(int todayRain, int tomorrowRain) => JsonDocument.Parse(
        $$"""
        { "Place": "Kraków, Polska",
          "Current": { "TempC": 21.0 },
          "Days": [ { "PrecipProb": {{todayRain}} }, { "PrecipProb": {{tomorrowRain}} } ] }
        """).RootElement;

    [Theory]
    [InlineData("Days.1.PrecipProb", ">=", "60", 10, 80, true)]   // tomorrow 80% ≥ 60 → fire
    [InlineData("Days.1.PrecipProb", ">=", "60", 10, 12, false)]  // tomorrow 12% → no fire
    [InlineData("Days.0.PrecipProb", ">=", "60", 90, 12, true)]   // today 90% → fire
    [InlineData("Current.TempC", ">", "20", 0, 0, true)]          // 21 > 20
    [InlineData("Current.TempC", "<", "20", 0, 0, false)]
    public void Numeric_comparisons(string path, string op, string val, int t0, int t1, bool expected) =>
        Assert.Equal(expected, JsonCondition.Evaluate(Weather(t0, t1), path, op, val));

    [Fact]
    public void Path_is_case_insensitive() =>
        Assert.True(JsonCondition.Evaluate(Weather(0, 70), "days.1.precipprob", ">=", "60"));

    [Fact]
    public void Missing_path_is_false_not_throw() =>
        Assert.False(JsonCondition.Evaluate(Weather(0, 0), "Days.9.PrecipProb", ">=", "1"));

    [Fact]
    public void Contains_matches_text()
    {
        var el = JsonDocument.Parse("""{ "Place": "Kraków, Polska" }""").RootElement;
        Assert.True(JsonCondition.Evaluate(el, "Place", "contains", "kraków"));
        Assert.False(JsonCondition.Evaluate(el, "Place", "contains", "Gdańsk"));
    }

    [Fact]
    public void Render_fills_matched_value() =>
        Assert.Equal("Jutro 80% szans na deszcz — weź parasol!",
            JsonCondition.Render("Jutro {value}% szans na deszcz — weź parasol!", Weather(0, 80), "Days.1.PrecipProb"));
}
