# E2E Test Flows — Regression Reference

This documents every flow exercised by `tests/e2e/e2e.sh` so the suite can be re-run as
regression. The script drives the **live HTTP API** exactly like the web chat does, against a
real Postgres and a real LLM (Azure AI Foundry / OpenAI-compatible).

## How to run

```bash
# 1. Postgres (pgvector image — the first migration installs the vector extension)
docker compose up -d postgres

# 2. LLM config — secrets live OUTSIDE the repo, never committed
#    ~/.agentplatform/config.json  (Llm.Endpoint, Llm.ApiKey, Llm.Models.{Small,Medium,Large})
#    Current setup: Azure AI Foundry, endpoint .../openai/v1, model gpt-5-mini for all tiers.

# 3. Start the API (writable blob path on macOS; container path /data is read-only)
ConnectionStrings__Postgres="Host=localhost;Database=agentplatform;Username=app;Password=localdev" \
  dotnet run --project src/AgentPlatform.Api --urls http://localhost:8080

# 4. Run the suite (resets DB, re-inits admin, runs all flows, prints PASS/FAIL, exit code = result)
bash tests/e2e/e2e.sh
```

The script self-resets state: `TRUNCATE users CASCADE` + family/audit tables, then
`POST /api/setup/init` to mint a fresh admin token. So it is idempotent and repeatable.

## Flows covered (20 assertions)

| # | Flow | Input (chat) | Expected | Exercises |
|---|------|--------------|----------|-----------|
| 1 | Auth — valid token | `GET /api/me` | `isAdmin:true` | UserTokenAuthenticator, primary-group resolution |
| 2 | Auth — bad token | `GET /api/me` wrong key | HTTP 401 | token rejection |
| 2a | Static root | `GET /` | HTTP 200, contains "Mój Agent" | `UseDefaultFiles()` maps `/` → wwwroot/index.html (regression) |
| 2b | Static admin page | `GET /stats.html` | HTTP 200 | static files middleware |
| 3 | Add payment | "dodaj nowy rachunek za prąd 230 zł termin 2026-08-01" | created (`prąd`) | router → ContextFirst planner → `family.payments.create` → audit |
| 4 | List payments | "co mam do zapłacenia?" | lists `prąd` | `family.list_payments` → `today.payments` provider → read tool |
| 5 | Mark paid — confirm prompt | "zapłaciłem rachunek za prąd" | `requiresConfirmation:true` + id | ConfirmationPolicy.Required → `pending_confirmations` |
| 6 | Mark paid — confirm | `POST /api/chat/confirm {confirmed:true}` | "Oznaczono jako zapłacone" | confirmation callback → idempotent `mark_paid` (first-wins) |
| 7 | Add task | "dodaj zadanie: wynieść śmieci" | acted (`✅`) | `family.tasks.create` |
| 8 | List tasks | "jakie mam zadania?" | shows `śmieci` | `family.list_tasks` → `today.tasks` |
| 9 | Add shopping item | "dodaj mleko do listy zakupów" | `mleko` | `family.shopping.add` (dedicated template) |
| 10 | List shopping | "co jest na liście zakupów?" | shows `mleko` | `family.list_shopping` → `group.shopping` |
| 11 | Add renewal | "dodaj przypomnienie: OC samochodu wygasa 2026-09-30" | `OC` | `family.renewals.add` (the long-horizon hook) |
| 12 | Remember fact | "zapamiętaj że mój numer licznika prądu to 123456" | `✅` | `user.remember_fact` → `memory_facts` |
| 13 | Incognito guard | `/incognito` then "dodaj rachunek za wodę 50 zł" | "incognito" rejection | ActionValidator blocks `HasSideEffects` tools; no audit/content written |
| 14 | Reset conversation | "zacznijmy od nowa" | "Rozmowa zresetowana" | ConversationResolver close+new, `conversation.reset` |
| 15 | Admin stats | `GET /admin/stats?days=30` | has `totalLlmCalls`, `totalActions>0` | metering + audit aggregation SQL |
| 16 | Stats records actions | (same) | `totalActions` ≠ 0 | audit_log written by ToolExecutor |
| 17 | Budget reset endpoint | `POST /api/admin/budget/reset {scopeKey:"global"}` | "Reset" or "not found" | admin-only breaker reset |

## Trust-boundary / safety properties asserted elsewhere (unit + integration)

These are covered by `dotnet test` (21 unit + 3 Testcontainers) and complement the E2E:
- RLS group isolation (a non-superuser role sees only its group's rows).
- Idempotency: same key twice = single execution.
- Validator: tool-not-in-AllowedTools rejected; role-below-minimum rejected; incognito blocks side-effects.
- ToolCalling diagnostic loop (business incident triage) through the unchanged core.
- tsvector full-text history search.

## Bugs found & fixed during E2E click-through

1. **EF Core version conflict** (`Relational 10.0.4` vs `10.0.9`) crashed the API on boot — pinned `10.0.9`.
2. **`IExecutionContextAccessor` scoped-from-root** at startup migration — switched to AsyncLocal singleton (RLS context flows with the async pipeline run + its DB connection).
3. **`ValidateContracts` resolved scoped tools from root** — now runs inside a scope.
4. **`/data/blobs` read-only on macOS** — blob path points at a writable dir locally.
5. **`/admin/stats` 500** — `SqlQueryRaw<T>` inherits snake_case convention; aliases switched to snake_case; dropped the digit-ambiguous `p95` column.
6. **Setup token not URL-safe** — base64 `+`/`/` broke `URLSearchParams` parsing in the web chat hash → Base64Url.
7. **Unhandled tool-input validation killed the whole run (504/empty)** — per-intent loop now catches `ToolInputValidationException` + generic, returns a friendly message.
8. **`CollectErrors` NRE** — `EvaluationResults.Details` is null in List output format; guarded.
9. **Planner: LLM args at top level instead of `toolInput`** — `PlanParser` now salvages top-level args into `toolInput`.
10. **Prompt templates never copied to `bin/`** — csproj glob used a Windows backslash (`prompts\**`); fixed to `/`. Template store now also resolves relative to `AppContext.BaseDirectory`.
11. **Field-name drift** (LLM used `item`/`expiry` vs schema `name`/`expiresOn`) — added explicit per-intent templates for shopping / renewals / memory.
12. **GPT-5 model constraints** — reasoning models reject custom `temperature` and `max_tokens`; provider now sends `max_completion_tokens` with a generous floor and omits temperature for `gpt-5*`/`o*` models.
13. **Root `/` returned 404** — `UseStaticFiles()` serves files by path but doesn't map `/` to the default document; the web chat URL is `/#key=...`. Added `app.UseDefaultFiles()`. Asserted by flows 2a/2b.
14. **Web chat login race** (preview only) — `init()` did a single `/api/me` fetch; if the server was still warming up it gave up and showed "Brak dostępu". `init()` in index.html + stats.html now retries transient failures (4×, 500 ms) and only stops immediately on 401/403 (a genuinely bad key).

## Shared shopping list + opt-in live notifications

Model (deliberately simple — matches how real homes work): **ONE shared household list**.
Everyone adds; everyone checks off; first-wins prevents double-buying. Notifications are
**off by default** — you only get pinged if you opt in.

**Data:** `family.shopping_items` (flat, group-scoped, RLS), `family.shopping_watchers`
(group_id, user_id, until?). `until = null` → standing opt-in; `until = now+TTL` → per-trip.
Mark/list/first-wins live in `ShoppingRepository`. Name→member match is `GroupDirectory`
(diacritic/declension-tolerant: "Agatą"→Agatha, "Olą"→Ola).

**Tools/intents:**
- `family.shopping.add` — "dodaj mleko"
- `family.shopping.list` — "co jest na liście zakupów?"
- `family.shopping.mark_bought` — "kupiłem mleko" → first-wins + notify active watchers (not the buyer)
- `family.shopping.notify_trip` — "jadę na zakupy, powiadom Agatę i Olę" → adds them as watchers with a TTL (default 4h)
- `family.shopping.watch` — "powiadamiaj mnie o zakupach" (standing on) / "nie powiadamiaj mnie" (off)

**Automated regression** (`AgentPlatform.Integration.Tests/ShoppingListTests`):
- shared add / list; first-wins mark (2nd mark returns null)
- watchers: standing active, per-trip active, expired excluded; remove works

**Manual / live regression** (multi-user SSE path, not in headless E2E):
1. Seed a 2nd member (e.g. Agatha) with a token in the same group.
2. As Sylwester: "dodaj mleko", "dodaj chleb".
3. As Sylwester (web chat open, subscribes `GET /api/stream?key=…`): "powiadamiaj mnie o zakupach" (opt in).
4. As Agatha (her token): "kupiłam mleko".
5. Expect Sylwester's chat shows live, no refresh: "🔔 🛒 Agatha kupił(a): mleko — Zostało: chleb". Verified in preview.
6. Default-silent check: with no watchers, a buy pings nobody.

Notes: reasoning models (gpt-5/o-series) are slow — synchronous HTTP wait is 90s. SSE is keyed by
user; the client dedupes echoed events. Push reaches web chat (SSE) + Telegram/Signal (if the member
has that id); an offline web-only member sees the update on next open.

### Tap-to-check (in-store UX — no chatting)

While shopping you just tap, you don't write. The checklist is a dedicated page
`wwwroot/shopping.html` (linked from the chat header "🛒 Lista") backed by **deterministic REST
endpoints that bypass the LLM** entirely (instant, free, audited, RLS-scoped, first-wins):
- `GET /api/shopping` — board (needed + bought-last-12h, with who bought)
- `POST /api/shopping/add` `{name,quantity?}`
- `POST /api/shopping/check` `{id}` — first-wins mark + notify watchers
- `POST /api/shopping/uncheck` `{id}` — undo
Live across people via SSE (`onmessage` → refetch). Verified in preview: tapping an item moves it to
"Kupione" with the buyer's name, instantly.

**Automated regression** (in `e2e.sh`, "Shopping checklist (tap, no LLM)" — deterministic, fast):
add → GET lists it (needed) → check (ok) → second check (ok:false, first-wins) → board shows bought →
uncheck → needed again.

### Self-contained plugins (logic + UI in one DLL)

A plugin ships everything — tools/handlers/providers, DB schema/migrations, **and its web UI** —
with **zero core changes**. Drop the DLL in and it works. Three generic host capabilities make this
possible (core has no domain knowledge):
1. `POST /api/action {tool, input}` — runs ANY plugin tool deterministically (ToolExecutor: schema
   validation, idempotency, audit, first-wins). No LLM. Server-side authz = the tool's `RequiredScope`.
2. Plugin-embedded web UI: the plugin assembly embeds `wwwroot/**` (manifest); the host serves it at
   `/plugins/{pluginId}/...` via `ManifestEmbeddedFileProvider`.
3. `IPluginUi` (SDK) + `GET /api/plugins/ui` — the plugin declares a launch tile (title/icon/entry);
   the web shell renders tiles dynamically and links to `/plugins/{id}/{entry}#key=…`.

The shopping checklist is the reference: its page lives in `AgentPlatform.Plugins.Family`
(`wwwroot/shopping/index.html`, embedded), served at `/plugins/family/shopping/index.html`, and talks
to core only through `/api/action` (`family.shopping.board/add/check/uncheck`). `Program.cs` has no
shopping code or `IShoppingRepository` reference — only `AddFamilyPlugins()`, like any plugin.

**Automated regression** (in `e2e.sh`, "Plugin action"): add → board(needed) → check(ok) →
second check(ok:false, first-wins) → uncheck(ok); `/api/plugins/ui` lists family; the plugin UI is
served from the DLL (`/plugins/family/shopping/index.html` → 200). Verified in preview: tile appears
in chat header, the DLL-served checklist renders, tapping marks bought (DB confirmed) via `/api/action`.

Channel story for tapping:
- **Web** ✅ — the checklist page above (primary in-store UX).
- **Telegram** — native inline buttons (one per item → callback → check). Channel supports it; wire when the bot runs.
- **Signal** ⚠️ — no interactive buttons in signal-cli-rest-api; fall back to numbered text or share the web checklist link.
- **WhatsApp** ⚠️ — interactive lists exist but the channel is deferred (Meta API cost/approval).

## Known limitations / not in automated E2E

- **LLM routing is non-deterministic.** Assertions check robust substrings, not exact text. Two
  consecutive runs were stable, but a model/prompt change can shift routing — the golden-set eval
  (`tests/AgentPlatform.Eval`) is the gate for that (runs when an LLM key is set).
- **Web search** (SearXNG) and **Signal** are not in the E2E (need extra containers). Start
  `docker compose up -d searxng signal-api` to exercise them manually.
- **Business plugin** (Teams/Jira/DevOps incident triage) is covered by the unit ToolCalling test;
  live diagnosis needs a DevOps logs/metrics backend configured.
- **Email** flow needs MailHog (`docker compose up -d mailhog`) + IMAP creds; not in E2E.
