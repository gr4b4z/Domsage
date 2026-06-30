---
name: spec-generator
description: |
  Generuje spec implementacyjny na podstawie opisu feature'a.
  Używaj gdy słyszysz: "mam pomysł", "chcę dodać", "nowa funkcjonalność",
  "napisz spec", "stwórz wymagania", "rozszerz plugin".
  Sam decyduje czy to plugin-only czy core change.
  Integruje się z GSD dla lekkiego, szybkiego flow.
allowed-tools: Read, Write, Edit, Glob, Grep, TodoWrite, Bash
---

# Spec Generator (GSD-integrated)

**Goal:** zamienić opis feature'a w precyzyjny, wykonywalny spec dla AgentPlatform — i przekazać go do
osobnej sesji GSD do implementacji. **Nigdy nie implementujesz tutaj.** Twój produkt to jeden plik
`docs/specs/YYYY-MM-DD-<feature>.md` + handoff.

**Filozofia GSD:** złożoność w systemie, nie w rozmowie. Czego można dowiedzieć się z kodu — sprawdź,
nie pytaj. Pytasz tylko o to, czego kod nie powie (intencja, przykłady, edge case'y, granice scope'u).

Wykonuj KROKI 1→4 **w kolejności**. Użyj `TodoWrite`, by śledzić: Wywiad → Analiza wpływu →
Spec → Handoff. HALT na końcu KROK 2 i KROK 3 i czekaj na potwierdzenie użytkownika.

---

## Fakty o tym repo (źródło prawdy dla analizy — cytuj je, nie zgaduj)

Architektura: core **zamknięty na modyfikację, otwarty na rozszerzenie**; każda zdolność to plugin.
Pipeline: IntentRouter → ContextBuilder → PlanningStrategy → PlanParser → ActionValidator (trust
boundary) → ToolExecutor → ResponseBuilder.

**Kontrakty SDK** (`sdk/AgentPlatform.PluginSdk/Contracts/`):
- Punkt wejścia: `IPluginRegistration` (`IPluginRegistration.cs:7`) — `Namespace`, opcjonalny `DbSchema`, `Register(IServiceCollection, IConfiguration)`.
- Zdolności (`Interfaces.cs`): `ITool` (akcja deterministyczna, jedyne co zmienia stan), `IIntentHandler` (deklaracja: Mode, AllowedTools, PromptTemplateId, Confirmation, opcj. `Description`/`PhraseResult`), `IContextProvider`, `IChannelPlugin`.
- Osobne pliki: `ISlashCommand`, `IScheduledJob`, `IWebhookHandler`, `IPluginUi`, `IOAuthTokenProvider`, `IOAuthCallbackHandler`, `IGroupTypeProvider`.

**Dwa idiomy rejestracji pluginu** (oba wpinane w `src/AgentPlatform.Api/Program.cs`):
1. Metoda rozszerzająca + skan Scrutor — np. `AddFamilyPlugins` (`FamilyPluginsExtensions.cs:16-24`).
2. `new <Name>PluginRegistration().Register(builder.Services, config.GetSection("Plugins:<Name>"))` — Weather/Business/Automation/Google/Calendar.

**Tryb Plannera DEKLARUJE handler**, Planner nie wybiera: `PlanningStrategy.ExecuteAsync` rozgałęzia
`handler.Mode == ContextFirst ? ContextFirstAsync : ToolCallingAsync` (`PlanningStrategy.cs:25`).
Tryby są dwa i stałe: `PlannerMode { ContextFirst, ToolCalling }` (`Enums.cs`). Router (IntentRouter)
odkrywa intencje z rejestru handlerów — **nowa intencja nie wymaga zmiany routera**.

**Shared context:** slice per-intencja → `IContextProvider` z dowolnym `ContextSlice(ProviderId, Scope,
object Data)` (`Models/CoreModels.cs:62`), wpięty w `handler.RequiredContextProviders` → trafia do
`{{context_json}}`. **Zero zmian core.** Dane *ambient* w samym `ExecutionContext` (rekord
`CoreModels.cs:43`) → zmiana SDK + wszystkich miejsc tworzenia kontekstu = **core change**.

**Schema isolation (plugin z DB):** w pełni plugin-only pod 3 warunkami:
1. własny DbContext z `HasDefaultSchema("<schema>")` (`FamilyDbContext.cs:32`);
2. rejestracja **także jako `DbContext`**: `services.AddScoped<DbContext>(sp => sp.GetRequiredService<XDbContext>())` (`FamilyPluginsExtensions.cs:38`) — inaczej host nie zmigruje tabel (`Program.cs:102`);
3. pierwsza migracja zawiera **RLS w SQL**: `ENABLE ROW LEVEL SECURITY` + `CREATE POLICY group_isolation … current_setting('app.current_group_id')` (`InitialFamily.cs:87-91`).
EF sam tworzy schemat (`migrationBuilder.EnsureSchema`, `InitialFamily.cs:14`) — **nie trzeba** ruszać
zaszytej tablicy `new[]{"family"}` w `Program.cs:99`. Realne ryzyko: **brak polityk RLS** = cicha utrata
izolacji grup (błąd poprawnościowy, nie build-error). Encje czytają scope z `ctx.GroupId`
(`PaymentTools.cs:37`); RLS dociska to po stronie DB (`RlsConnectionInterceptor.cs`).

**Czego NIE wolno ruszać przy plugin-only:** wszystko w `src/AgentPlatform.Core/Pipeline/*`,
kontrakty `sdk/AgentPlatform.PluginSdk/Contracts/*`, `src/AgentPlatform.Infrastructure/Postgres/AppDbContext.cs`
+ encje core + `RlsConnectionInterceptor.cs`. **Wolno** (composition root, nie Core): minimalne linie w
`Program.cs` (rejestracja + namespace w `PluginNamespaces`, `Program.cs:65`), `*.Api.csproj`, `AgentPlatform.slnx`.

**Konwencje testów:** xUnit, `[Fact]`/`[Theory]`, metody `Rzecz_RobiCos_WDanychWarunkach`, klasa
`<Cos>Tests`, namespace `AgentPlatform.Core.Tests`. Logika czysta + fake'i → `tests/AgentPlatform.Core.Tests/`
(np. `FakeConversationRepository`). Ścieżki z DB/RLS → `tests/AgentPlatform.Integration.Tests/` (Testcontainers).

**Plik ZAWSZE powstający** dla pluginu (minimalny, wzór Weather): `.csproj`, `<Name>.cs`
(ITool+IIntentHandler+IPluginRegistration), `GlobalUsings.cs` (alias `ExecutionContext`),
`src/AgentPlatform.Api/prompts/<intent_id>__v1.0.0.txt` (jeden na każdą intencję LLM-ową).

---

## KROK 1 — Szybki wywiad (minimum ceremonii)

Zadawaj pytania **JEDNO PO DRUGIM**, czekaj na odpowiedź. Nie pytaj o nic, co możesz sprawdzić w kodzie.
To **jedyne** pytania — resztę kontekstu wyciągasz sam w KROK 2.

- **P1:** „Opisz feature jednym zdaniem — co użytkownik może zrobić, czego nie mógł wcześniej?"
- **P2:** „Podaj konkretny przykład: wiadomość od użytkownika i oczekiwana odpowiedź systemu."
- **P3:** „Co się dzieje, gdy coś pójdzie nie tak? Podaj 2 edge case'y."
- **P4:** „Co jest świadomie POZA tym slice'em?"

Jeśli odpowiedź jest zbyt ogólna — **drąż**, nie przechodź dalej bez precyzji. Przykład „za ogólne":
P2 = „doda wpis" → dopytaj o dokładny tekst usera i dokładną odpowiedź systemu.

---

## KROK 2 — Automatyczna analiza wpływu (4 checki, równolegle)

Uruchom **cztery read-only checki jako jedną paczkę** `Grep`/`Read` (ten skill nie spawnuje subagentów —
`allowed-tools` jest read-only + Write). Każdy check zwraca **TAK/NIE + uzasadnienie z konkretnego
pliku i linii** — nigdy ogólnik.

**Check A — Plugin Contract Fit:** czy feature da się wyrazić istniejącymi kontraktami?
- Sprawdź: `ls sdk/AgentPlatform.PluginSdk/Contracts/` i dopasuj feature do `ITool`+`IIntentHandler`
  (przypadek typowy: nowa intencja+narzędzie), ew. `IScheduledJob`/`IWebhookHandler`/`ISlashCommand`/
  `IContextProvider`/`IOAuthTokenProvider`.
- **TAK** = mieści się w istniejącym kontrakcie. **NIE** = wymaga nowego *rodzaju* zdolności
  (nowy interfejs/zmiana istniejącego) → to zmiana SDK/core. Cytuj plik kontraktu.

**Check B — Planner Impact:** czy trzeba nowego trybu lub zmiany routingu?
- Tryby stałe (`Enums.cs PlannerMode`), wybór przez `handler.Mode` (`PlanningStrategy.cs:25`); router
  odkrywa intencje z rejestru.
- **NIE** = wystarczy zadeklarować `Mode` (ContextFirst/ToolCalling) na nowym handlerze. **TAK** = feature
  potrzebuje trzeciego trybu wykonania albo zmiany logiki IntentRoutera/PlanningStrategy → core.

**Check C — Schema Isolation:** jeśli feature trzyma dane — czy izolacja jest zachowana?
- Jeśli **bez DB** → C=TAK (n/d). Jeśli używa istniejącego repo/schematu pluginu → C=TAK.
- Jeśli **nowy schemat** → C=TAK tylko gdy spełnia 3 warunki (HasDefaultSchema + rejestracja jako
  `DbContext` + RLS w migracji). Sprawdź wzorzec: `FamilyDbContext.cs:32`, `FamilyPluginsExtensions.cs:38`,
  `InitialFamily.cs:87-91`.
- **NIE** = dane lądowałyby w core `AppDbContext` (schema `public`, bez polityki RLS per-grupa — jak
  `connected_accounts`/`automation_rules`, które są świadomymi prymitywami core) → core change.

**Check D — Dependency / blast-radius:** które istniejące pluginy mogą być dotknięte?
- `grep -rn "<istniejący tool/intent id>"` po `AllowedTools` i providerach; sprawdź `PluginNamespaces`
  (`Program.cs:65`) i czy feature reużywa cudzego toola (cross-namespace w AllowedTools jest dozwolone)
  lub współdzielonego repo/`IContextProvider`.
- Wynik: **lista pluginów** lub **BRAK**.

**Decyzja:**
- **PLUGIN ONLY** ⟺ A=TAK ∧ B=NIE ∧ C=TAK ∧ D=BRAK
- **CORE CHANGE** ⟺ B=TAK ∨ C=NIE ∨ A=NIE (D≠BRAK = ostrzeżenie, nie przesądza)

Jeśli którykolwiek check jest **niejednoznaczny** — nie zgaduj; zapisz do sekcji **Open Questions** spec'a.

Wyświetl użytkownikowi i **czekaj na potwierdzenie**:
```
Analiza wpływu:
  Tryb: [Plugin Only / Core Change Required]
  A (contract): TAK/NIE — <plik:linia>
  B (planner):  TAK/NIE — <plik:linia>
  C (schema):   TAK/NIE — <plik:linia>
  D (deps):     <lista pluginów / BRAK>
  Powód decyzji: <konkret, nie ogólnik>
```

---

## KROK 3 — Generowanie spec

Zapisz do **`docs/specs/YYYY-MM-DD-<feature-name>.md`** (utwórz katalog jeśli nie istnieje; datę pobierz
przez `Bash: date +%F`, nie zgaduj). Limit: **≤200 linii (Plugin Only)**, **≤300 (Core Change)**. Jeśli
więcej — podziel na dwa slice'y i powiedz o tym użytkownikowi.

Ścieżki w spec'u **muszą być realne** (z mapy plików wyżej), z namespace'ami. Edge case'y = z wywiadu
+ wykryte w Check D. Testy = w projekcie zgodnym z Check C (czysta logika → Core.Tests; DB/RLS → Integration.Tests).

### Szablon — PLUGIN ONLY
```markdown
# SPEC: <Feature Name>
Status: Draft
Tryb: Plugin Only
Data: <YYYY-MM-DD>

## Co i dlaczego
<jedno zdanie z P1>

## Przykład użycia
Użytkownik: "<z P2>"
System: "<z P2>"

## Edge Cases
- <z P3 #1>
- <z P3 #2>
- <ew. wykryte w Check D>

## Scope
IN:  <co robimy>
OUT: <z P4 — co pomijamy świadomie>

## Pliki do UTWORZENIA
- src/AgentPlatform.Plugins.<Name>/AgentPlatform.Plugins.<Name>.csproj
- src/AgentPlatform.Plugins.<Name>/<Name>.cs            # ITool <ns>.<tool> + IIntentHandler <ns>.<intent> + IPluginRegistration
- src/AgentPlatform.Plugins.<Name>/GlobalUsings.cs      # alias ExecutionContext
- src/AgentPlatform.Api/prompts/<intent_id>__v1.0.0.txt # SYSTEM/---/USER, {{allowed_tools_json}} {{context_json}} {{user_text}}
  <+ jeśli DB: Data/<Name>DbContext.cs (HasDefaultSchema), Data/Entities.cs, Data/Repositories.cs, Data/Migrations/* (RLS!)>

## Pliki do MODYFIKACJI
- src/AgentPlatform.Api/Program.cs        # rejestracja pluginu + dopisanie "<ns>" do PluginNamespaces (Program.cs:65)
- src/AgentPlatform.Api/AgentPlatform.Api.csproj + AgentPlatform.slnx   # referencja projektu
- README.md + docs/ARCHITECTURE.md        # JEŚLI feature jest widoczny dla użytkownika (kanał / capability / komenda / opcja configu) — katalog/tabela + sekcja architektury. Pomiń tylko dla zmian czysto wewnętrznych.

## Pliki KTÓRYCH NIE DOTYKAMY
- src/AgentPlatform.Core/Pipeline/*        # tryb deklaruje handler, router odkrywa intencję sam
- sdk/AgentPlatform.PluginSdk/Contracts/*  # feature mieści się w istniejących kontraktach (Check A)
- src/AgentPlatform.Infrastructure/Postgres/AppDbContext.cs + RlsConnectionInterceptor.cs

## Tests First
Plik: tests/AgentPlatform.<Core.Tests|Integration.Tests>/<Name>Tests.cs
Klasa: <Name>Tests   (xUnit, [Fact]/[Theory], metody Rzecz_Robi_WWarunkach)
Przypadki:
- <happy path z P2>
- <edge case 1 z P3>
- <edge case 2 z P3>

## Acceptance Criteria
- [ ] <kryterium 1 — testowalne>
- [ ] <kryterium 2 — testowalne>
- [ ] <kryterium 3 — testowalne>

## Open Questions
<tylko jeśli jakiś check był niejednoznaczny; inaczej "Brak">

## Kolejność implementacji
1. Napisz testy → commit jako checkpoint
2. <kroki w logicznej kolejności tego repo: kontrakty → tool → handler → prompt → rejestracja w Program.cs>
3. Wszystkie testy zielone (dotnet test)
4. Sprawdź AC jeden po drugim
```

### Dodatkowo dla CORE CHANGE (wstaw przed „Tests First")
```markdown
## Impact na Core
Problem: <co wymaga zmiany poza plugin scope — np. nowe pole w ExecutionContext, nowy PlannerMode, nowy kontrakt SDK>

## ADR
Opcja A: <pierwsza>
Opcja B: <druga>
Decyzja: <którą i dlaczego>
Konsekwencje dla istniejących pluginów: <lista — np. wszystkie miejsca tworzenia ExecutionContext>

## Migracja istniejących pluginów
<co zaktualizować i w jakiej kolejności — np. SDK → core call-sites → pluginy>
```

---

## KROK 4 — Handoff do GSD

Po zapisaniu spec'a wypisz **dokładnie** to (podstaw realną nazwę pliku i tryb):

```
✅ Spec: docs/specs/<filename>.md
Tryb: <Plugin Only / Core Change>

Otwórz NOWĄ sesję Claude Code i wklej:
```

— dla **PLUGIN ONLY**:
```
Zaimplementuj docs/specs/<filename>.md
Zacznij od testów, zrób commit przed implementacją,
implementuj aż wszystkie testy zielone.
Nie modyfikuj testów.
```

— dla **CORE CHANGE**:
```
Zaimplementuj docs/specs/<filename>.md
KOLEJNOŚĆ OBOWIĄZKOWA:
1. Zmiany core — commit
2. Migracja istniejących pluginów — commit
3. Nowy plugin — commit
4. Testy całości — muszą być zielone
Przy każdym kroku potwierdź, że poprzedni jest stabilny.
```

---

## Reguły (twarde)

- **Nigdy nie zaczynasz implementacji** — tylko spec + handoff.
- **Tylko realne ścieżki** z tego repo (mapa plików wyżej) — żadnych generycznych placeholderów typu `src/Plugins/Foo`.
- **Analiza wpływu cytuje plik:linia**, nie ogólniki.
- **Limity:** ≤200 (Plugin Only) / ≤300 (Core Change) linii. Większe → podziel na dwa slice'y.
- **Niejednoznaczny check → Open Questions**, nie zgadywanie.
- **Dokumentacja jest częścią feature'a:** jeśli zmiana jest widoczna dla użytkownika (nowy kanał, capability, komenda, opcja configu) — README.md i docs/ARCHITECTURE.md ZAWSZE trafiają do „Pliki do MODYFIKACJI" (EXTENDING.md tylko gdy zmienia się/dochodzi kontrakt SDK). Pomijaj docs wyłącznie dla zmian czysto wewnętrznych.
- **GSD:** złożoność w systemie, nie w rozmowie. Można sprawdzić w kodzie → sprawdź, nie pytaj.
