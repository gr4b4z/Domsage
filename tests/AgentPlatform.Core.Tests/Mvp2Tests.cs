using System.Net;
using System.Text;
using AgentPlatform.Core.Audit;
using AgentPlatform.Core.Budget;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.Pipeline;
using AgentPlatform.Core.Registry;
using AgentPlatform.Plugins.WebSearch;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentPlatform.Core.Tests;

public class ConversationResolverTests
{
    [Fact]
    public async Task ResetCommand_ClosesActive_AndCreatesNew()
    {
        var repo = new FakeConversationRepository
        {
            Active = new ConversationRecord(Guid.NewGuid(), "u", null, "test", false, "active", null, null, DateTime.UtcNow)
        };
        var resolver = new ConversationResolver(repo);
        var msg = new InputMessage("m", "test", "u", null, "/start", null, DateTimeOffset.UtcNow);
        var ctx = await resolver.ResolveAsync(msg, "u", null, CancellationToken.None);
        Assert.Contains(repo.Closed, c => c.reason == "user_reset");
        Assert.Single(repo.Created);
        Assert.False(ctx.IsIncognito);
    }

    [Fact]
    public async Task IncognitoCommand_CreatesIncognitoConversation()
    {
        var repo = new FakeConversationRepository();
        var resolver = new ConversationResolver(repo);
        var msg = new InputMessage("m", "test", "u", null, "/incognito", null, DateTimeOffset.UtcNow);
        var ctx = await resolver.ResolveAsync(msg, "u", null, CancellationToken.None);
        Assert.True(ctx.IsIncognito);
    }
}

public class IncognitoAuditTests
{
    [Fact]
    public async Task Incognito_ReadOnlyTool_WritesNoAudit()
    {
        var tool = new FakeTool("web.search", false);
        var services = new ServiceCollection();
        services.AddSingleton<ITool>(tool);
        var sp = services.BuildServiceProvider();
        var registry = new PluginRegistry([], [], NullLogger<PluginRegistry>.Instance);
        var auditRepo = new FakeAuditRepository();
        var executor = new ToolExecutor(registry, sp, new FakeIdempotencyRepository(),
            new FakeDeadLetterRepository(), new AuditLogger(auditRepo),
            new BudgetEnforcer(new FakeBudgetRepository(), Options.Create(new BudgetOptions())));

        var plan = new ActionPlan("i", PlannerMode.ContextFirst, ContextScope.User, null, "web.search",
            0.9, false, "k-inc", "v", ModelTier.Small, 0, System.Text.Json.JsonDocument.Parse("{}").RootElement);
        var ctx = TestCtx.Make(incognito: true);
        await executor.ExecuteAsync(plan, ctx, CancellationToken.None);

        Assert.Empty(auditRepo.Entries);
        BudgetEnforcer.Clear(ctx.RequestId);
    }
}

public class BraveSearchProviderTests
{
    private sealed class ScriptedHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        public List<string> SeenKeys { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            SeenKeys.Add(request.Headers.GetValues("X-Subscription-Token").First());
            return Task.FromResult(responses.Dequeue());
        }
    }

    [Fact]
    public async Task RateLimited_RetriesWithNextKey()
    {
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"web":{"results":[{"title":"T","url":"http://x","description":"d"}]}}""", Encoding.UTF8, "application/json")
        });
        var handler = new ScriptedHandler(responses);
        var http = new HttpClient(handler);
        var opts = Options.Create(new BraveOptions { ApiKeys = ["key-1", "key-2"] });
        var provider = new BraveSearchProvider(http, opts, new RoundRobinKeyStrategy(), NullLogger<BraveSearchProvider>.Instance);

        var results = await provider.SearchAsync("kurs EUR", 3, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(2, handler.SeenKeys.Count);
        Assert.NotEqual(handler.SeenKeys[0], handler.SeenKeys[1]);
    }
}
