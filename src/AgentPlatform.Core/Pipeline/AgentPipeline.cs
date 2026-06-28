using AgentPlatform.Core.Audit;
using AgentPlatform.Core.Budget;
using AgentPlatform.Core.Contracts;
using AgentPlatform.Core.Registry;
using AgentPlatform.PluginSdk.Contracts;
using AgentPlatform.PluginSdk.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Core.Pipeline;

/// <summary>Central orchestrator. One instance per message (Scoped).</summary>
public sealed class AgentPipeline(
    PluginRegistry registry,
    IUserRepository userRepo,
    IExecutionContextAccessor execAccessor,
    ConversationResolver convResolver,
    IntentRouter intentRouter,
    SmalltalkResponder smalltalk,
    ResultPhraser phraser,
    IEnumerable<ISlashCommand> slashCommands,
    ContextBuilder contextBuilder,
    Planner planner,
    ActionValidator validator,
    ToolExecutor toolExecutor,
    ResponseBuilder responseBuilder,
    ConversationWriter convWriter,
    IPendingIntentRepository pendingIntents,
    IPendingConfirmationRepository pendingConfirmations,
    IServiceProvider sp,
    ILogger<AgentPipeline> log)
{
    public async Task RunAsync(RawEvent rawEvent, CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString();
        var started = DateTimeOffset.UtcNow;
        PipelineRunResult? runResult = null;
        ExecutionContext? ctx = null;
        var lastIntent = "unknown";

        try
        {
            var channel = registry.GetChannel(rawEvent.ChannelId);
            var msg = await channel.ParseAsync(rawEvent, ct);

            var userInfo = await ResolveUserAsync(msg, ct);
            if (userInfo is null)
            {
                await channel.DeliverAsync(new OutputMessage(msg.ChannelId, msg.UserId,
                    "Nieznany użytkownik. Skontaktuj się z administratorem.", false, null, null)
                { RequestId = msg.MessageId }, ct);
                runResult = ErrorResult(requestId, msg, "unknown user", started);
                return;
            }

            var convCtx = await convResolver.ResolveAsync(msg, userInfo.UserId, userInfo.GroupId, ct);

            ctx = new ExecutionContext(
                RequestId: requestId, UserId: userInfo.UserId, GroupId: userInfo.GroupId,
                GroupType: userInfo.GroupType, UserRole: userInfo.Role, ChannelId: msg.ChannelId,
                ConversationId: convCtx.ConversationId, IsIncognito: convCtx.IsIncognito,
                StartedAt: started);
            execAccessor.Current = ctx;

            // 5a. Confirmation callback?
            if (msg.Text.StartsWith("confirm:", StringComparison.OrdinalIgnoreCase) ||
                msg.Text.StartsWith("cancel:", StringComparison.OrdinalIgnoreCase))
            {
                var resp = await HandleConfirmationAsync(msg, ctx, ct);
                await DeliverAndWriteAsync(channel, msg, resp, ctx, ct);
                runResult = OkResult(requestId, ctx, "confirmation", resp, started);
                return;
            }

            // 5a'. Deterministic slash command? (/connect-email, /connect-telegram, /help) — no LLM.
            if (msg.Text.TrimStart().StartsWith('/'))
            {
                var slash = await TryHandleSlashAsync(msg.Text.Trim(), ctx, ct);
                if (slash is not null)
                {
                    var r = new ResponseResult(slash, false, null, null);
                    await DeliverAndWriteAsync(channel, msg, r, ctx, ct);
                    runResult = OkResult(requestId, ctx, "slash", r, started);
                    return;
                }
            }

            // 5b. Pending clarify intent?
            var pending = await pendingIntents.GetActiveAsync(ctx.UserId, ct);
            if (pending is not null)
            {
                await pendingIntents.ClearAsync(pending.Id, ct);
                // Re-route the answer as a fresh message (simple resume strategy).
            }

            // 6. Route
            var matches = await intentRouter.ClassifyAsync(msg, ctx, ct);

            // 7. Execute each intent sequentially
            var responses = new List<ResponseResult>();
            foreach (var match in matches)
            {
                lastIntent = match.IntentId;
                if (match.IntentId == "clarify")
                {
                    var clarifyText = await planner.BuildClarifyQuestionAsync(match, ctx, ct);
                    await pendingIntents.SaveAsync(new PendingIntent(
                        ctx.UserId, ctx.GroupId,
                        match.MissingSlots.FirstOrDefault() ?? "clarify",
                        new Dictionary<string, string>(), match.MissingSlots), ct);
                    responses.Add(new ResponseResult(clarifyText, false, null, null));
                    continue;
                }
                if (match.IntentId == "fallback" || !registry.TryGetHandler(match.IntentId, out _))
                {
                    // No specific intent — answer conversationally (greetings, "who are you", chit-chat)
                    // instead of a blunt "I don't understand". Never invents household facts.
                    var reply = await smalltalk.RespondAsync(match.RawSegment, ctx, ct);
                    responses.Add(new ResponseResult(reply, false, null, null));
                    continue;
                }

                try
                {
                    responses.Add(await ExecuteSingleIntentAsync(match, msg, ctx, ct));
                }
                catch (BudgetExceededException ex)
                {
                    responses.Add(new ResponseResult($"⚠️ Limit budżetu osiągnięty: {ex.Message}", false, null, null));
                }
                catch (SecurityViolationException ex)
                {
                    log.LogWarning(ex, "Security violation in intent {Intent}", match.IntentId);
                    responses.Add(new ResponseResult("⛔ Brak uprawnień do tej akcji.", false, null, null));
                }
                catch (RetryableToolException)
                {
                    responses.Add(new ResponseResult("⏳ Akcja tymczasowo niedostępna, spróbuj ponownie.", false, null, null));
                }
                catch (TerminalToolException ex)
                {
                    responses.Add(new ResponseResult($"❌ Błąd: {ex.UserMessage}", false, null, null));
                }
                catch (ToolInputValidationException ex)
                {
                    log.LogWarning(ex, "Tool input validation failed for intent {Intent}", match.IntentId);
                    responses.Add(new ResponseResult(
                        "Nie mam wszystkich potrzebnych informacji. Doprecyzuj proszę szczegóły.", false, null, null));
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Unexpected error executing intent {Intent}", match.IntentId);
                    responses.Add(new ResponseResult("❌ Wystąpił błąd przy realizacji tej prośby.", false, null, null));
                }
            }

            var combined = CombineResponses(responses);
            await DeliverAndWriteAsync(channel, msg, combined, ctx, ct);
            runResult = OkResult(requestId, ctx, lastIntent, combined, started);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Pipeline failed for request {RequestId}", requestId);
            runResult = new PipelineRunResult(requestId, ctx?.UserId ?? "", ctx?.GroupId,
                rawEvent.ChannelId, lastIntent, PipelineRunStatus.Failed, null, null, ex.Message,
                0, 0, 0m, DateTimeOffset.UtcNow - started, DateTimeOffset.UtcNow);
        }
        finally
        {
            BudgetEnforcer.Clear(requestId);
            if (runResult is not null)
            {
                var hooks = sp.GetServices<IPipelineHook>();
                await Task.WhenAll(hooks.Select(async h =>
                {
                    try { await h.OnCompletedAsync(runResult, CancellationToken.None); }
                    catch (Exception ex) { log.LogError(ex, "Hook {Hook} failed", h.GetType().Name); }
                }));
            }
        }
    }

    private async Task<ResponseResult> ExecuteSingleIntentAsync(
        IntentMatch match, InputMessage msg, ExecutionContext ctx, CancellationToken ct)
    {
        var handler = registry.GetHandler(match.IntentId);
        var agentCtx = await contextBuilder.FetchAsync(
            new ContextRequest(ctx, match.IntentId, match.RawSegment), handler, ct);
        var plan = await planner.PlanAsync(msg with { Text = match.RawSegment }, handler, agentCtx, ctx, ct);

        if (plan.Intent == "clarify")
        {
            var clarifyText = await planner.BuildClarifyQuestionAsync(match, ctx, ct);
            return new ResponseResult(clarifyText, false, null, null);
        }

        var validated = await validator.ValidateAsync(plan, handler, ctx, ct);
        if (validated.Rejected)
            return new ResponseResult(validated.RejectionReason ?? "Akcja odrzucona.", false, null, null);

        if (validated.RequiresConfirmation)
        {
            var confId = await pendingConfirmations.SaveAsync(validated.Plan, ctx, ct);
            return new ResponseResult(validated.ConfirmationPrompt!, true, confId, ["✅ Tak", "❌ Anuluj"]);
        }

        var toolResult = await toolExecutor.ExecuteAsync(validated.Plan, ctx, ct);
        var built = await responseBuilder.BuildAsync(toolResult, validated.Plan, ctx, ct);

        // Optional: rephrase a successful tool result into a natural, question-aware answer.
        if (handler.PhraseResult && toolResult.Status == ToolResultStatus.Success
            && toolResult.Data is { } data && !built.RequiresConfirmation)
        {
            var phrased = await phraser.PhraseAsync(match.RawSegment, data.GetRawText(), built.Text, ctx, ct);
            return built with { Text = phrased };
        }
        return built;
    }

    private async Task<ResponseResult> HandleConfirmationAsync(
        InputMessage msg, ExecutionContext ctx, CancellationToken ct)
    {
        var parts = msg.Text.Split(':', 2);
        var confirmed = parts[0].Equals("confirm", StringComparison.OrdinalIgnoreCase);
        var confId = parts.Length > 1 ? parts[1] : msg.Text;

        var pending = await pendingConfirmations.GetAsync(confId, ct);
        if (pending is null)
            return new ResponseResult("❓ Nie znalazłem potwierdzenia. Może wygasło?", false, null, null);

        await pendingConfirmations.RecordSignalAsync(confId, confirmed ? "accepted" : "cancelled", null, ct);

        if (!confirmed)
        {
            await pendingConfirmations.ExpireAsync(confId, ct);
            return new ResponseResult("↩️ Anulowano.", false, null, null);
        }

        var toolResult = await toolExecutor.ExecuteAsync(pending.Plan, ctx, ct);
        return await responseBuilder.BuildAsync(toolResult, pending.Plan, ctx, ct);
    }

    // Returns the command's reply, or null if the text isn't a recognised slash command (so it falls
    // through to normal handling — e.g. /reset is consumed earlier by ConversationResolver).
    private async Task<string?> TryHandleSlashAsync(string text, ExecutionContext ctx, CancellationToken ct)
    {
        var sp = text.IndexOf(' ');
        var name = (sp < 0 ? text[1..] : text[1..sp]).ToLowerInvariant();
        var args = sp < 0 ? "" : text[(sp + 1)..].Trim();
        if (name == "help")
            return slashCommands.Any()
                ? "Dostępne komendy:\n" + string.Join("\n",
                    slashCommands.OrderBy(c => c.Name).Select(c => $"/{c.Name} — {c.Description}"))
                : "Brak dostępnych komend.";
        var cmd = slashCommands.FirstOrDefault(c => c.Name == name);
        return cmd is null ? null : await cmd.HandleAsync(args, ctx, ct);
    }

    private async Task<UserGroupInfo?> ResolveUserAsync(InputMessage msg, CancellationToken ct) =>
        msg.ChannelId switch
        {
            // "http" carries the platform user id directly.
            "http" => await userRepo.GetPrimaryGroupAsync(msg.UserId, ct),
            // Every messaging channel (telegram, signal, email, …) resolves via its generic channel identity.
            _ => await userRepo.GetByChannelIdentityAsync(msg.ChannelId, msg.UserId, ct)
        };

    private static ResponseResult CombineResponses(List<ResponseResult> responses)
    {
        if (responses.Count == 0) return new ResponseResult("…", false, null, null);
        if (responses.Count == 1) return responses[0];
        var text = string.Join("\n\n", responses.Select(r => r.Text));
        var conf = responses.FirstOrDefault(r => r.RequiresConfirmation);
        return new ResponseResult(text, conf is not null, conf?.ConfirmationId, conf?.Actions);
    }

    private async Task DeliverAndWriteAsync(IChannelPlugin channel, InputMessage msg,
        ResponseResult response, ExecutionContext ctx, CancellationToken ct)
    {
        await channel.DeliverAsync(new OutputMessage(msg.ChannelId, msg.UserId, response.Text,
            response.RequiresConfirmation, response.ConfirmationId, response.Actions)
        { RequestId = msg.MessageId }, ct);

        await convWriter.WriteAsync(ctx.ConversationId, ctx.IsIncognito,
            msg.Text, response.Text, intent: null, actionSummary: null, tokens: 0, ct);
    }

    private static PipelineRunResult OkResult(string requestId, ExecutionContext ctx, string intent,
        ResponseResult resp, DateTimeOffset started)
    {
        var status = ctx.IsIncognito ? PipelineRunStatus.Incognito
            : resp.RequiresConfirmation ? PipelineRunStatus.Clarify
            : PipelineRunStatus.Success;
        return new PipelineRunResult(requestId, ctx.UserId, ctx.GroupId, ctx.ChannelId, intent,
            status, null, null, null, 0, 0, 0m, DateTimeOffset.UtcNow - started, DateTimeOffset.UtcNow);
    }

    private static PipelineRunResult ErrorResult(string requestId, InputMessage msg, string error,
        DateTimeOffset started) =>
        new(requestId, msg.UserId, msg.GroupId, msg.ChannelId, "unknown", PipelineRunStatus.Rejected,
            null, null, error, 0, 0, 0m, DateTimeOffset.UtcNow - started, DateTimeOffset.UtcNow);
}
