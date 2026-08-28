# Model Map

Model IDs are **configuration, not constants**. This file is the single place they are
defined; re-run `/setup` (or edit here and re-emit) when providers rotate model names.

## Tier assignments

| Tier | Roles | Rationale |
|------|-------|-----------|
| `frontier` | orchestrator (main session), `evaluator`, `qa-evaluator`, `diagnostician` | Delegation and evaluation have a capability floor — don't cheap out on the roles that decide and verify; continuation-ladder diagnosis needs frontier-tier reasoning |
| `light` | `implementer`, `test-writer`, `terminal` | Checklist-driven execution and compact command running retain most quality at a fraction of the cost when the plan and the verification are strong |

## Resolved models

Only Claude Code is installed in this project, so this is the only column.

| Tier | Claude Code |
|------|-------------|
| `frontier` | `claude-opus-5` |
| `light` | `claude-sonnet-5` |

Claude Code accepts either the family alias (`opus`, `sonnet`, `haiku`, `fable`) or a
full model ID in agent frontmatter. Full IDs are pinned here so a rotation is a visible
edit to this file rather than a silent change in what the alias resolves to.

Reasoning-effort pins: evaluators and the diagnostician `high`, workers `medium`, via
Claude Code's `effort:` frontmatter key.

**Env overrides beat this file.** `CLAUDE_CODE_SUBAGENT_MODEL` and
`CLAUDE_CODE_EFFORT_LEVEL` override agent frontmatter, and an org `availableModels`
allowlist can cause a pin to be ignored entirely. If an evaluator's verdict looks
off-tier, check those before suspecting the map.

## Fallback chains

| Tier | Chain (first available wins) |
|------|------------------------------|
| `frontier` | `claude-opus-5` → `claude-fable-5` → `claude-sonnet-5` → (omit pin — harness default) |
| `light` | `claude-sonnet-5` → `claude-haiku-4-5-20251001` → (omit pin — harness default) |

Rules (adapted from bradygaster/squad's model selector):

- **Never fall back UP a tier** — a light role must never silently land on a frontier
  model, and a frontier role never degrades below the standard rung of its chain.
- Fallbacks are silent to the user but **logged in the orchestration log**.
- Max 3 fallback attempts, then omit the model pin and take the harness default.
- Apply at most **one** complexity adjustment per spawn — no cascading bumps.
- **Cost ceiling policy**: an EXPLICIT human model choice above a plan/admin ceiling →
  warn loudly and honor it; an implicit (auto-selected) choice → downgrade to the
  ceiling; no compliant model at all → fail loud — never silently substitute.
- **Prompts are executable — treat like code**: tasks that author or edit agent, skill,
  or prompt files use the frontier tier, never the docs/light tier.

## Fix-cycle rotation (after evaluator rejections)

A model blind to its own class of mistake rarely fixes it on retry. Rotate:

| Fix cycle | Implementer model |
|-----------|-------------------|
| 1 | `claude-sonnet-5` (normal) |
| 2 | `claude-fable-5` — a different model **family** at comparable tier (analytical diversity) |
| 3 | `claude-opus-5` |

### After cycle 3 — continuation ladder pins

When fix cycles 1–3 exhaust, the continuation ladder (see ORCHESTRATION.md) applies:

| Ladder step | Model pin |
|-------------|-----------|
| Diagnosis-driven implementation attempt (rung 4) | `claude-opus-5` |
| Evaluator arbitration mode (rung 5) | `claude-fable-5` — a different model **family** from the primary evaluator at frontier tier (analytical diversity at the capability floor) |

**On "different family" inside Claude Code.** Claude Code runs Claude models only, so
analytical diversity here means a different *model lineage*, not a different vendor.
`claude-fable-5` fills both rotation slots — it is the non-Opus/Sonnet lineage available
at frontier-comparable capability — while `claude-opus-5` remains the primary frontier
pin. Rotation is deliberately a lineage change, never merely a size change: bumping
`claude-sonnet-5` to `claude-opus-5` is the cycle-3 escalation, not the cycle-2
diversity step.

## Harness caveats that shape tiering

- **Env overrides**: `CLAUDE_CODE_SUBAGENT_MODEL` and `CLAUDE_CODE_EFFORT_LEVEL`
  override agent frontmatter, and an org `availableModels` allowlist can cause a pin to
  be ignored silently. Both defeat this map — check them first when an agent behaves
  off-tier.
- **Spawn depth**: `.claude/settings.json` pins
  `CLAUDE_CODE_MAX_SUBAGENT_SPAWN_DEPTH=3`; the harness default has flipped before, and
  a lower value silently breaks worker-spawns-`terminal`.
- **Verifying a rotation**: after editing this file, re-pin every
  `.claude/agents/*.md` `model:` value from it and confirm with
  `grep -h '^model:' .claude/agents/*.md | sort -u` — the map is the source of truth,
  but nothing enforces it automatically.
