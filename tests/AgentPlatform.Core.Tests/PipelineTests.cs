using System.Text.Json;
using AgentPlatform.Core.Audit;
using AgentPlatform.Core.Budget;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.Pipeline;
using AgentPlatform.Core.Registry;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentPlatform.Core.Tests;

public class BudgetEnforcerTests
{
    private static BudgetEnforcer Make(FakeBudgetRepository repo, BudgetOptions? opts = null) =>
        new(repo, Options.Create(opts ?? new BudgetOptions()));

    [Fact]
    public async Task CheckRequest_Throws_WhenScopeTripped()
    {
        var repo = new FakeBudgetRepository();
        repo.States["global"] = new BudgetState("global", 999m, true);
        var enforcer = Make(repo);
        await Assert.ThrowsAsync<BudgetExceededException>(
            () => enforcer.CheckRequestAsync("r1", null, CancellationToken.None));
    }

    [Fact]
    public async Task CheckIteration_Throws_AfterMaxIterations()
    {
        var enforcer = Make(new FakeBudgetRepository(), new BudgetOptions { PerRequestMaxIterations = 2 });
        await enforcer.CheckIterationAsync("rA", CancellationToken.None);
        await enforcer.CheckIterationAsync("rA", CancellationToken.None);
        await Assert.ThrowsAsync<BudgetExceededException>(
            () => enforcer.CheckIterationAsync("rA", CancellationToken.None));
        BudgetEnforcer.Clear("rA");
    }

    [Fact]
    public async Task RecordSpend_Trips_WhenOverCap()
    {
        var repo = new FakeBudgetRepository();
        var enforcer = Make(repo, new BudgetOptions { GlobalKillSwitchUsd = 1m });
        await enforcer.RecordSpendAsync(null, 2m, CancellationToken.None);
        Assert.True(repo.States["global"].Tripped);
    }
}

public class ActionValidatorTests
{
    private static (ActionValidator validator, ServiceProvider sp) Build(params ITool[] tools)
    {
        var services = new ServiceCollection();
        foreach (var t in tools) services.AddSingleton(t);
        var sp = services.BuildServiceProvider();
        var registry = new PluginRegistry([], [], NullLogger<PluginRegistry>.Instance);
        var budget = new BudgetEnforcer(new FakeBudgetRepository(), Options.Create(new BudgetOptions()));
        return (new ActionValidator(registry, sp, budget), sp);
    }

    private static ActionPlan Plan(string toolId, double conf = 0.9) =>
        new("intent", PlannerMode.ContextFirst, ContextScope.Group, null, toolId, conf,
            false, "k", "v", ModelTier.Small, 0, JsonDocument.Parse("{}").RootElement);

    [Fact]
    public async Task Rejects_ToolNotInAllowedTools()
    {
        var (validator, _) = Build(new FakeTool("family.payments.delete", true));
        var handler = new FakeHandler("i", ["family.payments.mark_paid"]);
        await Assert.ThrowsAsync<SecurityViolationException>(
            () => validator.ValidateAsync(Plan("family.payments.delete"), handler, TestCtx.Make(), CancellationToken.None));
    }

    [Fact]
    public async Task Blocks_SideEffectTool_InIncognito()
    {
        var (validator, _) = Build(new FakeTool("family.payments.mark_paid", true));
        var handler = new FakeHandler("i", ["family.payments.mark_paid"]);
        var result = await validator.ValidateAsync(Plan("family.payments.mark_paid"), handler,
            TestCtx.Make(incognito: true), CancellationToken.None);
        Assert.True(result.Rejected);
    }

    [Fact]
    public async Task Rejects_WhenRoleBelowMinimum()
    {
        var (validator, _) = Build(new FakeTool("family.x", true, MemberRole.Admin));
        var handler = new FakeHandler("i", ["family.x"]);
        await Assert.ThrowsAsync<SecurityViolationException>(
            () => validator.ValidateAsync(Plan("family.x"), handler,
                TestCtx.Make(role: MemberRole.Member), CancellationToken.None));
    }

    [Fact]
    public async Task RequiresConfirmation_WhenPolicyRequired()
    {
        var (validator, _) = Build(new FakeTool("family.x", true));
        var handler = new FakeHandler("i", ["family.x"], confirm: ConfirmationPolicy.Required);
        var result = await validator.ValidateAsync(Plan("family.x"), handler, TestCtx.Make(), CancellationToken.None);
        Assert.True(result.RequiresConfirmation);
        Assert.NotNull(result.ConfirmationPrompt);
    }
}

public class ToolExecutorTests
{
    [Fact]
    public async Task Idempotency_SecondCall_DoesNotReExecute()
    {
        var tool = new FakeTool("family.x", true);
        var services = new ServiceCollection();
        services.AddSingleton<ITool>(tool);
        var sp = services.BuildServiceProvider();
        var registry = new PluginRegistry([], [], NullLogger<PluginRegistry>.Instance);
        var idem = new FakeIdempotencyRepository();
        var audit = new AuditLogger(new FakeAuditRepository());
        var budget = new BudgetEnforcer(new FakeBudgetRepository(), Options.Create(new BudgetOptions()));
        var executor = new ToolExecutor(registry, sp, idem, new FakeDeadLetterRepository(), audit, budget);

        var plan = new ActionPlan("i", PlannerMode.ContextFirst, ContextScope.Group, null,
            "family.x", 0.9, false, "key-1", "v", ModelTier.Small, 0, JsonDocument.Parse("{}").RootElement);
        var ctx = TestCtx.Make();

        await executor.ExecuteAsync(plan, ctx, CancellationToken.None);
        await executor.ExecuteAsync(plan, ctx, CancellationToken.None);

        Assert.Equal(1, tool.Executions);
        BudgetEnforcer.Clear(ctx.RequestId);
    }
}

public class PlanParserTests
{
    private readonly PlanParser _parser = new(NullLogger<PlanParser>.Instance);

    [Fact]
    public void InvalidJson_ReturnsClarify()
    {
        var handler = new FakeHandler("i", ["t"]);
        var plan = _parser.Parse("not json at all", handler, TestCtx.Make(), "v1");
        Assert.Equal("clarify", plan.Intent);
    }

    [Fact]
    public void ValidJson_ParsesPlan()
    {
        var handler = new FakeHandler("family.add_payment", ["family.payments.create"]);
        var json = """{"tool":"family.payments.create","confidence":0.92,"toolInput":{"creditor":"PGNiG","amount":120}}""";
        var plan = _parser.Parse(json, handler, TestCtx.Make(), "v1");
        Assert.Equal("family.payments.create", plan.ToolId);
        Assert.Equal(0.92, plan.Confidence, 3);
    }
}

public class PlanResponseValidatorTests
{
    [Fact]
    public void OutOfRangeConfidence_BecomesClarify()
    {
        var v = new PlanResponseValidator();
        var handler = new FakeHandler("i", ["t"]);
        var bad = new ActionPlan("i", PlannerMode.ContextFirst, ContextScope.Group, null, "t",
            5.0, false, "k", "v", ModelTier.Small, 0, JsonDocument.Parse("{}").RootElement);
        var result = v.Validate(bad, handler, TestCtx.Make());
        Assert.Equal("clarify", result.Intent);
    }
}

public class ContextBuilderTests
{
    private sealed class GroupProvider : IContextProvider
    {
        public string ProviderId => "today.payments";
        public ContextScope Scope => ContextScope.Group;
        public Task<ContextSlice> FetchAsync(ContextRequest req, CancellationToken ct) =>
            Task.FromResult(new ContextSlice(ProviderId, Scope, new { }));
    }

    [Fact]
    public async Task GroupScopedProvider_NoGroup_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IContextProvider>(new GroupProvider());
        var sp = services.BuildServiceProvider();
        var registry = new PluginRegistry([], [], NullLogger<PluginRegistry>.Instance);
        var builder = new ContextBuilder(registry, sp);
        var handler = new FakeHandler("i", []) ;
        // handler with a group provider required
        var h2 = new HandlerWithProviders("i", ["today.payments"]);
        var ctx = TestCtx.Make() with { GroupId = "" };
        await Assert.ThrowsAsync<ContextScopeViolationException>(
            () => builder.FetchAsync(new ContextRequest(ctx, "i", "txt"), h2, CancellationToken.None));
    }

    private sealed class HandlerWithProviders(string id, string[] providers) : IIntentHandler
    {
        public string IntentId => id;
        public PlannerMode Mode => PlannerMode.ContextFirst;
        public string[] RequiredContextProviders => providers;
        public string[] AllowedTools => [];
        public string PromptTemplateId => id;
        public ModelTier PreferredTier => ModelTier.Small;
        public ConfirmationPolicy Confirmation => ConfirmationPolicy.NotRequired;
    }
}
