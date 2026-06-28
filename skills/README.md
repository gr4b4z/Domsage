# Skills — runtime, no-code extensions

A **skill** adds a new capability without recompiling: drop a folder into
`~/.agentplatform/skills/<name>/` (or set `Skills:Path`) containing:

- `skill.json` — the manifest
- `prompt.txt` — the prompt (`SYSTEM` / `---` / `USER`, with `{{allowed_tools_json}}`, `{{user_text}}`)

The host discovers it at startup and registers it as an intent. A skill is **routing + a prompt +
an allow-list of existing, vetted tools** — there is no shell or arbitrary code. The trust boundary,
idempotency, budget and confirmation all still apply, exactly like a compiled handler.

## Manifest

| Field | Meaning |
|---|---|
| `id` | becomes the intent `skill.<id>` |
| `description` | one-line hint the router uses to match user phrasings |
| `mode` | `ContextFirst` (one LLM call → tool) or `ToolCalling` |
| `allowedTools` | tool ids the skill may call — **must already exist** |
| `confirmation` | `NotRequired` / `Required` / `RequiredForHighImpact` |
| `tier` | `Local` / `Small` / `Medium` / `Large` |
| `phraseResult` | `true` → rephrase the tool result into a natural answer |

See [`outfit-advisor/`](outfit-advisor/) for a working example (suggests what to wear from the
weather, reusing the `weather.current` tool — zero code).

A skill referencing an unknown tool fails fast at startup (clear contract error); a malformed skill
is skipped, never fatal.
