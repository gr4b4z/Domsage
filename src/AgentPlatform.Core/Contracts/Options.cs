namespace AgentPlatform.Core.Contracts;

public sealed class BudgetOptions
{
    public decimal PerRequestMaxCostUsd { get; set; } = 0.05m;
    public int PerRequestMaxLlmCalls { get; set; } = 5;
    public int PerRequestMaxToolCalls { get; set; } = 10;
    public int PerRequestMaxIterations { get; set; } = 10;
    public decimal PerHouseholdDailyCapUsd { get; set; } = 1.00m;
    public decimal PerHouseholdMonthlyCapUsd { get; set; } = 15.00m;
    public decimal GlobalKillSwitchUsd { get; set; } = 100.00m;
    public int MaxWebSearchCallsPerRequest { get; set; } = 3;
}

public sealed class PromptOptions
{
    public string TemplatesPath { get; set; } = "prompts/";
    public Dictionary<string, string> ActiveVersions { get; set; } = new();
}
