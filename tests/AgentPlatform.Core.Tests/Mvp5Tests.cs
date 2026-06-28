using System.Text.Json;
using AgentPlatform.Core.Audit;
using AgentPlatform.Core.Budget;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.Pipeline;
using AgentPlatform.Core.Registry;
using AgentPlatform.Plugins.Business;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentPlatform.Core.Tests;

file sealed class ScriptedLlmProvider(Queue<LlmResult> responses) : ILlmProvider
{
    public int Calls;
    public string ProviderId => "scripted";
    public ModelTier Tier => ModelTier.Large;
    public PriceCard Price => new(0, 0, 0);
    public Task<LlmResult> CompleteAsync(LlmRequest req, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(responses.Dequeue());
    }
}

file sealed class FakeDiagnostics : IDiagnosticsBackend
{
    public Task<string> FetchPipelineLogsAsync(string id, CancellationToken ct) =>
        Task.FromResult("ERROR: step 'restore' failed: NU1101 package not found");
    public Task<string> FetchMetricsAsync(string id, CancellationToken ct) => Task.FromResult("cpu=12%");
}

public class BusinessToolCallingTests
{
    [Fact]
    public async Task IncidentTriage_RunsDiagnosticLoop_ThenProducesFinalPlan_ViaUnchangedCore()
    {
        // Business plugin tools registered against the SDK — core is untouched.
        var services = new ServiceCollection();
        services.AddSingleton<IDiagnosticsBackend, FakeDiagnostics>();
        services.AddScoped<ITool, FetchPipelineLogsTool>();
        services.AddScoped<ITool, AnnotateFailureTool>();

        // Scripted LLM: 1) call a diagnostic tool, 2) emit the final annotate plan.
        var responses = new Queue<LlmResult>();
        responses.Enqueue(new LlmResult(null, 10, 5, 0,
            new ToolCallRequest("workspace.devops.fetch_pipeline_logs",
                JsonSerializer.SerializeToElement(new { pipelineRunId = "8821" }))));
        responses.Enqueue(new LlmResult(
            """{"tool":"workspace.devops.annotate_failure","confidence":0.91,"target":"8821","toolInput":{"pipelineRunId":"8821","diagnosis":"missing nuget package"}}""",
            12, 8, 0, null));
        services.AddKeyedSingleton<ILlmProvider>(ModelTier.Large, new ScriptedLlmProvider(responses));

        var sp = services.BuildServiceProvider();
        var registry = new PluginRegistry([], [], NullLogger<PluginRegistry>.Instance);
        var budget = new BudgetEnforcer(new FakeBudgetRepository(), Options.Create(new BudgetOptions()));
        var meter = new NoOpMeter();
        var executor = new ToolExecutor(registry, sp, new FakeIdempotencyRepository(),
            new FakeDeadLetterRepository(), new AuditLogger(new FakeAuditRepository()), budget);
        var parser = new PlanParser(NullLogger<PlanParser>.Instance);
        var strategy = new PlanningStrategy(sp, meter, budget, parser, new PlanResponseValidator(), executor);

        var handler = new IncidentTriageHandler();
        var prompt = new PromptBuilder.BuiltPrompt(
            new LlmRequest("gpt-4o", ModelTier.Large, 0.2, null, null, null,
                [new("system", "diagnose"), new("user", "pipeline 8821 failed")], null), "v1");
        var ctx = TestCtx.Make() with { GroupId = Guid.NewGuid().ToString(), UserId = Guid.NewGuid().ToString() };
        var msg = new InputMessage("m", "teams", "u", ctx.GroupId, "pipeline 8821 failed", null, DateTimeOffset.UtcNow);

        var plan = await strategy.ExecuteAsync(prompt, handler, msg, ctx, CancellationToken.None);

        Assert.Equal("workspace.devops.annotate_failure", plan.ToolId);
        Assert.True(plan.DiagnosticSteps >= 1);   // the LLM iterated over a diagnostic tool
        BudgetEnforcer.Clear(ctx.RequestId);
    }

    private sealed class NoOpMeter : IUsageMeter
    {
        public Task RecordAsync(UsageEvent e, CancellationToken ct) => Task.CompletedTask;
        public Task<SpendSnapshot> GetSpendAsync(BudgetScope s, Window w, CancellationToken ct) =>
            Task.FromResult(new SpendSnapshot(s, 0, false));
    }
}
