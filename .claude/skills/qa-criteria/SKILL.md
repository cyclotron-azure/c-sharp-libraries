---
name: qa-criteria
description: Criteria the qa-evaluator applies to user-facing behavioral evidence for Cyclotron C# Libraries. Reference skill — not invoked directly.
disable-model-invocation: true
---

# QA Evaluation Criteria

Evaluate observed behavior from the user's perspective. Evidence not provided = behavior
not tested = an issue.

**Hard-threshold rule**: the criteria below are gates, not averages — one failing
surface **or cross-cutting check** caps the verdict at ISSUES FOUND no matter how clean
the rest are. A secrets leak, for example, caps the verdict on its own, regardless of how
clean every surface is.

## Per-surface criteria

This project's only user-facing surface is the **public package API** of
`Cyclotron.Graph.Core` and `Cyclotron.Graph.Mail` — there is no CLI and no UI, so the
CLI/web sections below do not apply and any surface list that claims them is wrong.
"The user" is a developer in a downstream repo who installed the NuGet package and read
its README. Evidence must be gathered from **outside the assembly** (public surface
only, never `internal`), with HTTP faked at `StubHttpMessageHandler`. The
`Graph/samples/Cyclotron.Graph.Sample` harness needs real Entra credentials and is a
manual developer aid, never QA evidence.

### CLI / command surfaces
- Exact invocation shown; output is what a user would expect and understand.
- Failure modes produce actionable messages (no raw tracebacks) and correct exit codes.
- Machine-readable output modes (e.g. `--json`) emit valid, complete structures.

### API surfaces
- Every touched route responds; defined routes never 404/5xx under normal input.
- Auth-required routes reject unauthenticated calls (401 is healthy there).
- Response bodies are well-formed and match the documented shape.

### Web UI surfaces
- Screenshots show the affected views rendering correctly.
- Browser console is free of errors; network log shows no failed requests.

## Cross-cutting checks

- **Effect verification**: an operation that reports success demonstrably had its effect.
- **No secrets** in any output, log, or screenshot.
- **Consistency**: the same information reported by two surfaces agrees.

## Verdicts

- **PASS**: all affected surfaces evidenced and clean.
- **ISSUES FOUND**: numbered issues, each with observed vs expected and exact repro
  steps — these become fix tasks for implementer.
- **REJECT**: an auto-fail trigger fired (see `.claude/agents/qa-evaluator.md`) or the
  evidence shows the feature fundamentally doesn't work.
