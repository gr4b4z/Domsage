#!/usr/bin/env bash
# End-to-end regression suite — drives the live API exactly like a user would.
#
# Prereqs:
#   docker compose up -d postgres
#   ~/.agentplatform/config.json with a working Llm endpoint+key (Azure/OpenAI)
#   API running:  dotnet run --project src/AgentPlatform.Api --urls http://localhost:8080
#
# Usage:  tests/e2e/e2e.sh            (resets DB, re-inits, runs all flows)
#         BASE=http://localhost:8080 tests/e2e/e2e.sh
#
# Exit code 0 = all assertions passed. Each flow is documented in tests/e2e/FLOWS.md.

set -u
BASE="${BASE:-http://localhost:8080}"
PG_CONTAINER="${PG_CONTAINER:-domsage-postgres-1}"
PASS=0; FAIL=0; FAILED_NAMES=()

say()  { printf "\n\033[1m== %s ==\033[0m\n" "$1"; }
ok()   { PASS=$((PASS+1)); printf "  \033[32mPASS\033[0m %s\n" "$1"; }
bad()  { FAIL=$((FAIL+1)); FAILED_NAMES+=("$1"); printf "  \033[31mFAIL\033[0m %s\n     got: %s\n" "$1" "$2"; }
# assert <name> <haystack> <needle>
assert() { case "$2" in *"$3"*) ok "$1";; *) bad "$1" "$2";; esac; }

chat()    { curl -s -m 120 -X POST "$BASE/api/chat"         -H "X-Api-Key: $KEY" -H "Content-Type: application/json" -d "{\"text\":$1}"; }
confirm() { curl -s -m 120 -X POST "$BASE/api/chat/confirm" -H "X-Api-Key: $KEY" -H "Content-Type: application/json" -d "$1"; }
# Portable field extractor (BSD/GNU): "field":"value" -> value
jq_field(){ printf '%s' "$1" | grep -o "\"$2\":\"[^\"]*\"" | head -1 | sed 's/.*:"//; s/"$//'; }

# ── 0. Reset state for deterministic run ────────────────────────────────────
say "Setup: reset DB + init admin"
docker exec -i "$PG_CONTAINER" psql -U app -d agentplatform -c "TRUNCATE users CASCADE;" >/dev/null 2>&1
# family.* + audit are separate tables — clear what the flows touch
docker exec -i "$PG_CONTAINER" psql -U app -d agentplatform -c \
  "TRUNCATE family.payments, family.tasks, family.shopping_items, family.renewals, audit_log, usage_meter_events, pending_confirmations, conversations, conversation_messages, budget_states RESTART IDENTITY CASCADE;" >/dev/null 2>&1

SETUP=$(curl -s -X POST "$BASE/api/setup/init" -H "Content-Type: application/json" -d '{"name":"Sylwester","groupName":"Dom"}')
KEY=$(printf '%s' "$SETUP" | sed -n 's/.*#key=\([^"]*\)".*/\1/p')
if [ -z "$KEY" ]; then echo "FATAL: no key from setup/init: $SETUP"; exit 2; fi
echo "  key acquired"

# ── 1. Auth ─────────────────────────────────────────────────────────────────
say "Auth"
ME=$(curl -s "$BASE/api/me" -H "X-Api-Key: $KEY")
assert "me returns admin"        "$ME" '"isAdmin":true'
UNAUTH=$(curl -s -o /dev/null -w "%{http_code}" "$BASE/api/me" -H "X-Api-Key: wrong-key")
assert "bad key -> 401"          "$UNAUTH" "401"

# ── 1b. Static web UI is served at root (regression: UseDefaultFiles) ────────
say "Static UI"
ROOT_CODE=$(curl -s -o /tmp/root.html -w "%{http_code}" "$BASE/")
assert "GET / -> 200"            "$ROOT_CODE" "200"
assert "root serves chat UI"     "$(cat /tmp/root.html)" "Mój Agent"
assert "stats.html served"       "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/stats.html")" "200"

# ── 2. Payments: add -> list -> mark paid (confirmation flow) ────────────────
say "Payments"
R=$(chat '"dodaj nowy rachunek za prąd 230 zł termin 2026-08-01"')
assert "add_payment confirms/acts" "$R" "prąd"
R=$(chat '"co mam do zapłacenia?"')
assert "list_payments shows prąd"  "$R" "prąd"

R=$(chat '"zapłaciłem rachunek za prąd"')
CID=$(jq_field "$R" confirmationId)
if printf '%s' "$R" | grep -q '"requiresConfirmation":true' && [ -n "$CID" ]; then
  ok "mark_payment_paid asks confirmation"
  R2=$(confirm "{\"confirmationId\":\"$CID\",\"confirmed\":true}")
  assert "confirm -> paid" "$R2" "zapłaco"
else
  bad "mark_payment_paid asks confirmation" "$R"
fi

# ── 3. Tasks: add -> list ────────────────────────────────────────────────────
say "Tasks"
R=$(chat '"dodaj zadanie: wynieść śmieci"')
assert "add_task acts"  "$R" "✅"
R=$(chat '"jakie mam zadania?"')
assert "list_tasks shows task" "$R" "śmieci"

# ── 4. Shopping: add -> list ──────────────────────────────────────────────────
say "Shopping"
R=$(chat '"dodaj mleko do listy zakupów"')
assert "add_shopping acts" "$R" "mleko"
R=$(chat '"co jest na liście zakupów?"')
assert "list_shopping shows milk" "$R" "mleko"

# ── 5. Renewals ───────────────────────────────────────────────────────────────
say "Renewals"
R=$(chat '"dodaj przypomnienie: OC samochodu wygasa 2026-09-30"')
assert "add_renewal acts" "$R" "OC"

# ── 6. Long-term memory fact ──────────────────────────────────────────────────
say "Memory facts"
R=$(chat '"zapamiętaj że mój numer licznika prądu to 123456"')
assert "remember_fact acts" "$R" "✅"

# ── 7. Incognito: side-effect tools blocked, no content persisted ────────────
say "Incognito"
chat '"/incognito"' >/dev/null
R=$(chat '"dodaj rachunek za wodę 50 zł"')
assert "incognito blocks side-effect" "$R" "incognito"
chat '"/incognito off"' >/dev/null

# ── 8. Reset conversation ─────────────────────────────────────────────────────
say "Reset"
R=$(chat '"zacznijmy od nowa"')
assert "reset acknowledged" "$R" "resetowana"

# ── 8b. Plugin checklist via generic /api/action (deterministic, no LLM) ─────
say "Plugin action (/api/action, no LLM)"
action() { curl -s -X POST "$BASE/api/action" -H "X-Api-Key: $KEY" -H "Content-Type: application/json" -d "$1"; }
ADD=$(action '{"tool":"family.shopping.add","input":{"name":"jajka"}}')
assert "action add -> ok"             "$ADD" '"ok":true'
BOARD=$(action '{"tool":"family.shopping.board","input":{}}')
assert "board lists item"             "$BOARD" "jajka"
assert "item is needed"               "$BOARD" '"status":"needed"'
# Target jajka specifically (the item this section created), not whatever is first on the list.
IID=$(printf '%s' "$BOARD" | grep -oE '"id":"[^"]*","name":"jajka"' | head -1 | sed -E 's/"id":"([^"]*)".*/\1/')
[ -n "$IID" ] && ok "board returns item id" || bad "board returns item id" "$BOARD"
# check + second-check prove the item was marked bought and first-wins holds (deterministic).
assert "check -> ok"                  "$(action "{\"tool\":\"family.shopping.check\",\"input\":{\"itemId\":\"$IID\"}}")" '"ok":true'
assert "second check -> not ok (first-wins)" "$(action "{\"tool\":\"family.shopping.check\",\"input\":{\"itemId\":\"$IID\"}}")" '"ok":false'
assert "uncheck -> ok"                "$(action "{\"tool\":\"family.shopping.uncheck\",\"input\":{\"itemId\":\"$IID\"}}")" '"ok":true'
assert "plugins/ui lists family"      "$(curl -s "$BASE/api/plugins/ui" -H "X-Api-Key: $KEY")" '"pluginId":"family"'
assert "plugin UI served from DLL"    "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/plugins/family/shopping/index.html")" "200"

# ── 9. Admin dashboard reflects activity ─────────────────────────────────────
say "Admin stats"
STATS=$(curl -s "$BASE/admin/stats?days=30" -H "X-Api-Key: $KEY")
assert "stats has llm calls"  "$STATS" '"totalLlmCalls"'
# at least one successful action recorded
case "$STATS" in *'"totalActions":0'*) bad "stats records actions" "$STATS";; *'"totalActions"'*) ok "stats records actions";; *) bad "stats records actions" "$STATS";; esac

# ── 10. Admin budget reset endpoint ──────────────────────────────────────────
say "Budget reset"
BR=$(curl -s -X POST "$BASE/api/admin/budget/reset" -H "X-Api-Key: $KEY" -H "Content-Type: application/json" -d '{"scopeKey":"global"}')
# 'global' may not exist yet -> NotFound is acceptable; a tripped+reset would return message
case "$BR" in *"Reset"*|*"not found"*) ok "budget reset endpoint responds";; *) bad "budget reset endpoint responds" "$BR";; esac

# ── Summary ───────────────────────────────────────────────────────────────────
say "Summary"
printf "  PASSED: %d   FAILED: %d\n" "$PASS" "$FAIL"
if [ "$FAIL" -gt 0 ]; then printf "  failing: %s\n" "${FAILED_NAMES[*]}"; exit 1; fi
echo "  ALL GREEN"
