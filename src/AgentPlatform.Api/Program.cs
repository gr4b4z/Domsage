using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentPlatform.Api;
using AgentPlatform.Core.DI;
using AgentPlatform.Core.Pipeline;
using AgentPlatform.Core.Registry;
using AgentPlatform.Infrastructure.DI;
using AgentPlatform.Infrastructure.Llm;
using AgentPlatform.Infrastructure.Postgres;
using AgentPlatform.Plugins.Family.DI;
using AgentPlatform.Plugins.Http;
using AgentPlatform.Plugins.Email;
using AgentPlatform.Plugins.Email.DI;
using AgentPlatform.Plugins.Http.DI;
using AgentPlatform.Plugins.Signal.DI;
using AgentPlatform.Plugins.Telegram;
using AgentPlatform.Plugins.Telegram.DI;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// User config layer (~/.agentplatform/config.json) — optional so dev/CI can run from appsettings + env.
var userConfigPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".agentplatform", "config.json");
builder.Configuration
    .AddJsonFile(userConfigPath, optional: true)
    .AddEnvironmentVariables();

// 1. Core
builder.Services.AddAgentPlatformCore(builder.Configuration);
// 2. Infrastructure (DbContext, repositories, meter, blob, scheduler, RLS interceptor)
builder.Services.AddAgentPlatformInfrastructure(builder.Configuration);
// 3. LLM providers (keyed by tier)
builder.Services.AddLlmProviders(builder.Configuration);
// 4. Channel plugins (both built-in)
builder.Services.AddTelegramPlugin(builder.Configuration);
builder.Services.AddHttpChannelPlugin(builder.Configuration);
builder.Services.AddSignalPlugin(builder.Configuration);
builder.Services.AddEmailPlugin(builder.Configuration);
// 5. Family domain plugins
builder.Services.AddFamilyPlugins(builder.Configuration);
// 5b. Web search plugin (registered via its IPluginRegistration).
new AgentPlatform.Plugins.WebSearch.WebSearchPluginRegistration()
    .Register(builder.Services, builder.Configuration.GetSection("Plugins:WebSearch"));
// 5c. Business domain — MVP5, registered as a pure plugin (no core change).
new AgentPlatform.Plugins.Business.WorkspacePluginRegistration()
    .Register(builder.Services, builder.Configuration.GetSection("Plugins:Workspace"));

// Known plugin namespaces for contract validation.
builder.Services.AddSingleton(new PluginNamespaces(["family", "web", "workspace", "telegram"]));

// 6. Scheduler (Hangfire)
var connStr = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Database=agentplatform;Username=app;Password=localdev";
builder.Services.AddHangfire(cfg => cfg.UsePostgreSqlStorage(
    o => o.UseNpgsqlConnection(connStr),
    new PostgreSqlStorageOptions { SchemaName = "hangfire" }));
builder.Services.AddHangfireServer();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler(e => e.Run(async ctx =>
{
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new { error = "Internal server error" });
}));

// Migrate core + plugin DbContexts at startup.
await using (var scope = app.Services.CreateAsyncScope())
{
    var sp = scope.ServiceProvider;
    var coreDb = sp.GetRequiredService<AppDbContext>();
    foreach (var schema in new[] { "family" })
        await coreDb.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schema}\"");
    await coreDb.Database.MigrateAsync();
    foreach (var pluginCtx in sp.GetServices<DbContext>().Where(c => c is not AppDbContext))
        await pluginCtx.Database.MigrateAsync();
}

// Validate plugin contracts.
var registry = app.Services.GetRequiredService<PluginRegistry>();
foreach (var ns in app.Services.GetRequiredService<PluginNamespaces>().Namespaces)
    registry.Namespaces.Add(ns);
using (var validateScope = app.Services.CreateScope())
    registry.ValidateContracts(validateScope.ServiceProvider);

// Recurring jobs — discovered generically from any plugin's IScheduledJob. The host has no
// knowledge of specific plugins; each job (id + cron + work) ships in its plugin DLL.
var recurring = app.Services.GetRequiredService<IRecurringJobManager>();
using (var jobScope = app.Services.CreateScope())
    foreach (var job in jobScope.ServiceProvider.GetServices<IScheduledJob>())
        recurring.AddOrUpdate<AgentPlatform.Core.Scheduler.ScheduledJobRunner>(
            job.JobId, r => r.RunAsync(job.JobId, CancellationToken.None), job.Cron);

app.UseDefaultFiles();   // map "/" -> wwwroot/index.html (web chat entry)
app.UseStaticFiles();

// Serve each plugin's own web UI straight from its DLL (embedded wwwroot) at /plugins/{id}/...
foreach (var ui in app.Services.GetServices<IPluginUi>())
{
    var provider = new Microsoft.Extensions.FileProviders.ManifestEmbeddedFileProvider(ui.AssetAssembly, "wwwroot");
    app.UseFileServer(new FileServerOptions
    {
        FileProvider = provider,
        RequestPath = $"/plugins/{ui.PluginId}",
        EnableDefaultFiles = true
    });
}

async Task<AuthResult?> Authenticate(HttpContext ctx, UserTokenAuthenticator auth, CancellationToken ct)
{
    var token = ctx.Request.Headers["X-Api-Key"].FirstOrDefault()
             ?? ctx.Request.Query["key"].FirstOrDefault();
    return await auth.AuthenticateAsync(token, ct);
}

// Plugin-owned public webhooks: every IWebhookHandler maps its own route. The host stays generic —
// it knows nothing about Telegram/Stripe/etc.; the plugin validates its own secret and handles the body.
foreach (var wh in app.Services.GetServices<IWebhookHandler>())
{
    var handler = wh; // singletons, captured for the endpoint closure
    app.MapPost(handler.Route, async (HttpRequest req, CancellationToken ct) =>
    {
        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync(ct);
        var headers = req.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var res = await handler.HandleAsync(new WebhookRequest(headers, body), ct);
        return res.Body is null ? Results.StatusCode(res.StatusCode) : Results.Content(res.Body, statusCode: res.StatusCode);
    });
}

// Server-Sent Events — live web-chat updates (shopping notifications, etc.)
app.MapGet("/api/stream", async (HttpContext ctx, UserTokenAuthenticator auth,
    AgentPlatform.Core.Contracts.ISseHub hub, CancellationToken ct) =>
{
    var s = await Authenticate(ctx, auth, ct);
    if (s is null) { ctx.Response.StatusCode = 401; return; }
    ctx.Response.Headers.Append("Content-Type", "text/event-stream");
    ctx.Response.Headers.Append("Cache-Control", "no-cache");
    ctx.Response.Headers.Append("X-Accel-Buffering", "no");
    await ctx.Response.WriteAsync(": connected\n\n", ct);
    await ctx.Response.Body.FlushAsync(ct);
    try
    {
        await foreach (var payload in hub.Subscribe(s.UserId, ct))
        {
            await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { /* client disconnected */ }
});

// ── Generic deterministic action — runs ANY plugin tool, no LLM, no domain knowledge ──
// This is the single extension point plugin UIs call. Core knows nothing about shopping.
static ExecutionContext ActionCtx(AuthResult s) => new(
    Guid.NewGuid().ToString(), s.UserId, s.GroupId, s.GroupType, s.UserRole,
    "web", "", false, DateTimeOffset.UtcNow);

app.MapPost("/api/action", async (
    [FromBody] ActionRequest body,
    [FromServices] UserTokenAuthenticator auth,
    [FromServices] AgentPlatform.Core.Contracts.IExecutionContextAccessor exec,
    [FromServices] PluginRegistry registry,
    [FromServices] AgentPlatform.Core.Pipeline.ToolExecutor executor,
    HttpContext ctx, CancellationToken ct) =>
{
    var s = await Authenticate(ctx, auth, ct);
    if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(body.Tool)) return Results.BadRequest(new { error = "tool required" });

    ITool tool;
    try { tool = registry.ResolveTool(body.Tool, ctx.RequestServices); }
    catch { return Results.NotFound(new { error = $"unknown tool '{body.Tool}'" }); }

    // Server-side authorization: the tool's own scope/role requirement (never the client's claim).
    if (s.UserRole < tool.RequiredScope.MinimumRole) return Results.Forbid();

    exec.Current = ActionCtx(s);
    var input = body.Input.ValueKind == JsonValueKind.Object ? body.Input
        : JsonDocument.Parse("{}").RootElement;
    var plan = new ActionPlan(
        Intent: "action." + body.Tool, Mode: PlannerMode.ContextFirst, Scope: ContextScope.Group,
        TargetId: null, ToolId: body.Tool, Confidence: 1.0, RequiresConfirmation: false,
        IdempotencyKey: Guid.NewGuid().ToString(), PromptVersion: "action", ModelTier: ModelTier.Local,
        DiagnosticSteps: 0, ToolInput: input);

    try
    {
        var result = await executor.ExecuteAsync(plan, exec.Current, ct);
        return Results.Ok(new { ok = true, data = result.Data, message = result.HumanMessage });
    }
    catch (AgentPlatform.Core.Contracts.SecurityViolationException) { return Results.Forbid(); }
    catch (AgentPlatform.Core.Contracts.ToolInputValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) { return Results.Ok(new { ok = false, message = ex.Message }); }
});

// Plugin-provided web UIs (each plugin ships its own page inside its DLL).
app.MapGet("/api/plugins/ui", async (
    [FromServices] UserTokenAuthenticator auth,
    [FromServices] IEnumerable<IPluginUi> uis,
    HttpContext ctx, CancellationToken ct) =>
{
    var s = await Authenticate(ctx, auth, ct);
    if (s is null) return Results.Unauthorized();
    return Results.Ok(uis.Select(u => new
    {
        pluginId = u.PluginId, title = u.Title, icon = u.Icon,
        url = $"/plugins/{u.PluginId}/{u.EntryPath}"
    }));
});

app.MapGet("/api/me", async (
    [FromServices] UserTokenAuthenticator auth,
    [FromServices] IOptions<AgentPlatform.Plugins.Telegram.TelegramOptions> tg,
    HttpContext ctx, CancellationToken ct) =>
{
    var s = await Authenticate(ctx, auth, ct);
    // Linking is only usable when a bot token is set AND polling is on (the /start handler lives in the poller).
    var telegramLinkable = !string.IsNullOrEmpty(tg.Value.BotToken) && tg.Value.UsePolling;
    return s is null ? Results.Unauthorized()
        : Results.Ok(new
        {
            s.UserId, s.Name, s.GroupId, s.GroupType,
            isInvite = s.TokenLabel == "invite",
            isAdmin = s.UserRole == MemberRole.Admin,
            telegramLinkable
        });
});

// Mint a one-time code to link this user's Telegram chat: send "/start <code>" to the bot.
app.MapPost("/api/me/telegram-link", async (
    [FromServices] UserTokenAuthenticator auth,
    [FromServices] AgentPlatform.Plugins.Telegram.TelegramLinkStore links,
    [FromServices] IOptions<AgentPlatform.Plugins.Telegram.TelegramOptions> tg,
    HttpContext ctx, CancellationToken ct) =>
{
    var s = await Authenticate(ctx, auth, ct);
    if (s is null) return Results.Unauthorized();
    var code = links.Mint(s.UserId);
    var botUsername = tg.Value.BotUsername;
    var deepLink = string.IsNullOrEmpty(botUsername) ? null : $"https://t.me/{botUsername}?start={code}";
    return Results.Ok(new { code, command = $"/start {code}", deepLink, botUsername });
});

app.MapPost("/api/chat", async (
    [FromBody] HttpChatBody body, [FromServices] IMessageBus bus,
    [FromServices] HttpChannelPlugin channel, [FromServices] UserTokenAuthenticator auth,
    HttpContext ctx, CancellationToken ct) =>
{
    var s = await Authenticate(ctx, auth, ct);
    if (s is null) return Results.Unauthorized();
    var mid = Guid.NewGuid().ToString();
    await bus.PublishAsync(new RawEvent("http",
        JsonSerializer.Serialize(new HttpChannelRequest(mid, s.UserId, s.GroupId, body.Text))), ct);
    var response = await channel.WaitForResponseAsync(s.UserId, mid, ct);
    return response is null ? Results.StatusCode(504)
        : Results.Ok(new HttpChatResponse(response.Text, response.ConfirmationRequired, response.ConfirmationId));
});

app.MapPost("/api/chat/confirm", async (
    [FromBody] HttpConfirmBody body, [FromServices] IMessageBus bus,
    [FromServices] HttpChannelPlugin channel, [FromServices] UserTokenAuthenticator auth,
    HttpContext ctx, CancellationToken ct) =>
{
    var s = await Authenticate(ctx, auth, ct);
    if (s is null) return Results.Unauthorized();
    var text = body.Confirmed ? $"confirm:{body.ConfirmationId}" : $"cancel:{body.ConfirmationId}";
    var mid = Guid.NewGuid().ToString();
    await bus.PublishAsync(new RawEvent("http",
        JsonSerializer.Serialize(new HttpChannelRequest(mid, s.UserId, s.GroupId, text))), ct);
    var response = await channel.WaitForResponseAsync(s.UserId, mid, ct);
    return response is null ? Results.StatusCode(504) : Results.Ok(new { response.Text });
});

app.MapPost("/api/setup/init", async (
    [FromBody] SetupInitRequest body, [FromServices] AppDbContext db,
    [FromServices] IConfiguration config, CancellationToken ct) =>
{
    if (await db.Users.AnyAsync(ct))
        return Results.Conflict(new { error = "Already initialized" });

    var userId = Guid.NewGuid();
    var groupId = Guid.NewGuid();
    // URL-safe token (no +, /, = ) — survives URLSearchParams parsing in the web chat hash.
    var rawToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
    var baseUrl = config["WebChat:BaseUrl"] ?? "http://localhost:8080";

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    db.Users.Add(new() { Id = userId, DisplayName = body.Name });
    db.Groups.Add(new() { Id = groupId, Type = "household", Name = body.GroupName });
    db.GroupMembers.Add(new() { GroupId = groupId, UserId = userId, Role = "admin" });
    db.UserTokens.Add(new() { UserId = userId, TokenHash = hash, Label = "web" });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return Results.Ok(new { userId, groupId, webChatUrl = $"{baseUrl}/#key={rawToken}" });
});

app.MapPost("/api/admin/budget/reset", async (
    [FromBody] BudgetResetRequest body, [FromServices] AppDbContext db,
    [FromServices] UserTokenAuthenticator auth, HttpContext ctx, CancellationToken ct) =>
{
    var session = await Authenticate(ctx, auth, ct);
    if (session is null) return Results.Unauthorized();
    if (session.UserRole != MemberRole.Admin) return Results.Forbid();
    var affected = await db.Database.ExecuteSqlRawAsync(
        "UPDATE budget_states SET tripped = FALSE, spent_usd = 0, reset_at = NOW() WHERE scope_key = {0}",
        body.ScopeKey);
    return affected > 0 ? Results.Ok(new { message = $"Reset: {body.ScopeKey}" })
        : Results.NotFound(new { error = "Scope key not found" });
});

app.MapGet("/admin/stats", async (
    [FromServices] AppDbContext db, [FromServices] UserTokenAuthenticator auth,
    [FromQuery] int days, HttpContext ctx, CancellationToken ct) =>
{
    var session = await Authenticate(ctx, auth, ct);
    if (session is null) return Results.Unauthorized();
    if (session.UserRole != MemberRole.Admin) return Results.Forbid();
    days = Math.Clamp(days == 0 ? 30 : days, 1, 365);
    var since = DateTimeOffset.UtcNow.AddDays(-days);

    var llmDaily = await db.Database.SqlQueryRaw<LlmDailyRow>(
        """
        SELECT DATE(occurred_at AT TIME ZONE 'Europe/Warsaw') AS day, COUNT(*)::int AS calls,
               SUM(input_tokens)::bigint AS input_tokens, SUM(output_tokens)::bigint AS output_tokens,
               ROUND(SUM(cost_usd)::numeric, 4) AS cost_usd, model_tier AS model_tier
        FROM usage_meter_events WHERE occurred_at >= {0}
        GROUP BY DATE(occurred_at AT TIME ZONE 'Europe/Warsaw'), model_tier ORDER BY 1 DESC
        """, since).ToListAsync(ct);

    var toolStats = await db.Database.SqlQueryRaw<ToolStatsRow>(
        """
        SELECT tool_id AS tool_id, COUNT(*)::int AS total_calls,
               COUNT(*) FILTER (WHERE result = 'success')::int AS successes,
               COUNT(*) FILTER (WHERE result = 'failed')::int AS failures,
               COALESCE(ROUND(AVG(cost_usd)::numeric, 6), 0) AS avg_cost_usd
        FROM audit_log WHERE occurred_at >= {0} AND tool_id IS NOT NULL
        GROUP BY tool_id ORDER BY 2 DESC LIMIT 20
        """, since).ToListAsync(ct);

    var intentQuality = await db.Database.SqlQueryRaw<IntentQualityRow>(
        """
        SELECT intent AS intent, COUNT(*)::int AS total,
               COUNT(*) FILTER (WHERE eval_signal = 'accepted')::int AS accepted,
               COUNT(*) FILTER (WHERE eval_signal = 'corrected')::int AS corrected,
               COUNT(*) FILTER (WHERE eval_signal = 'cancelled')::int AS cancelled,
               COUNT(*) FILTER (WHERE eval_signal = 'ignored')::int AS ignored,
               COALESCE(ROUND(AVG(cost_usd)::numeric, 6), 0) AS avg_cost_usd
        FROM audit_log WHERE occurred_at >= {0} GROUP BY intent ORDER BY 2 DESC LIMIT 20
        """, since).ToListAsync(ct);

    var breakers = await db.Database.SqlQueryRaw<BreakerRow>(
        """
        SELECT scope_key AS scope_key, spent_usd AS spent_usd, tripped AS tripped,
               tripped_at AS tripped_at, window_start AS window_start
        FROM budget_states WHERE tripped = TRUE OR spent_usd > 0 ORDER BY spent_usd DESC
        """).ToListAsync(ct);

    var dlq = await db.Database.SqlQueryRaw<DlqRow>(
        """
        SELECT tool_id AS tool_id, error_type AS error_type, COUNT(*)::int AS count,
               MAX(occurred_at) AS last_at
        FROM dead_letter_queue WHERE resolved = FALSE AND occurred_at >= {0}
        GROUP BY tool_id, error_type ORDER BY 3 DESC
        """, since).ToListAsync(ct);

    var summary = new
    {
        totalCostUsd = llmDaily.Sum(r => r.CostUsd),
        totalLlmCalls = llmDaily.Sum(r => r.Calls),
        totalActions = toolStats.Sum(r => r.TotalCalls),
        successRate = toolStats.Sum(r => r.TotalCalls) == 0 ? 0
            : (double)toolStats.Sum(r => r.Successes) / toolStats.Sum(r => r.TotalCalls) * 100,
        trippedBreakers = breakers.Count(b => b.Tripped),
        unresolvedErrors = dlq.Sum(d => d.Count)
    };
    return Results.Ok(new { summary, llmDaily, toolStats, intentQuality, breakers, dlq, days });
});

app.MapHealthChecks("/healthz");
app.UseHangfireDashboard("/hangfire");

app.Run();

/// <summary>Holds the set of known plugin namespaces for contract validation.</summary>
public sealed record PluginNamespaces(string[] Namespaces);

public partial class Program;
