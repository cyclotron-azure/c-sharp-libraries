---
name: align-docs
description: Align all repository documentation to shipped code after a feature is implemented — Phase 6 of the Cyclotron C# Libraries orchestration lifecycle. Updates every markdown surface (READMEs, docs/, agent/skill files) EXCEPT _research/ and _goals/ (orchestrator-managed, read-only). Runs after the final audit and Quality Checks pass, or when the user asks to sync documentation with recent changes.
disable-model-invocation: true
---

# Align Docs (Phase 6)

Bring repository **documentation** back in sync with shipped **code** after a feature
lands. Same evaluator-gated subagent workflow as the `feature` skill (discover → plan →
evaluate → execute-per-doc → final audit), but the deliverable is **accurate Markdown,
never code**.

## Core guardrails (NON-NEGOTIABLE — repeat in every subagent context package)

1. **HARD EXCLUSION — `_research/` and `_goals/`.** Never create, edit, or delete
   anything under these paths. You **may READ** them as source-of-truth for what
   shipped; you must **never WRITE** to them.
2. **DOCS-ONLY.** Never modify code, infra, tests, or config. If a doc claims behavior
   that does not exist, **fix the doc** — never change code to match the doc.
3. **ANTI-INVENTION.** Every documented claim must trace to a file you actually read
   this run. If a detail is unconfirmed, **omit it** — do not guess routes, flags,
   names, or behavior.
4. **SCOPE DISCIPLINE.** Update only **stale or missing** content. Preserve
   already-accurate prose **verbatim**. Prefer surgical edits over rewrites; a
   justified no-op for an already-correct doc is a valid outcome.
5. **HISTORICAL LOGS.** Dated notes under `_research/` are append/annotate territory,
   not rewrite-history. Promote only implemented ground truth into `Graph/README.md`.
This project keeps no rendered siblings (no `.html` mirrors) — every doc is markdown
and is edited directly. It does, however, keep **layered READMEs that repeat each
other**, and those must stay consistent: a fact stated in more than one README is
edited in every copy in the same pass, or in none.

## Documentation surface map

In scope: **everything markdown EXCEPT `_research/` and `_goals/`** — READMEs, docs
under `docs/`, per-directory instruction files, and `.claude`
skill/agent files.

| Code area changed | Docs that must be checked |
|-------------------|---------------------------|
| `Graph/src/<PackageId>/` public surface (new/changed/removed public type, member, or DI extension) | `Graph/src/<PackageId>/README.md` — registration API, configuration reference, public surface |
| `Graph/src/<PackageId>/Options/` (new option, changed default, changed validation) | that package's `README.md` configuration table; `Graph/samples/Cyclotron.Graph.Sample/README.md` if it names the setting; `appsettings.json` in the sample if the key shape changed |
| A new package added to a family, or the dependency direction touched | `Graph/README.md` (package table + dependency direction + which package to install), root `README.md` library-families table, `CyclotronAzure.Libraries.sln` |
| A new family folder | root `README.md` — library-families table and folder convention |
| `Directory.Build.props`, `Directory.Packages.props`, `nuget.config`, a csproj's `MinVerTagPrefix` | root `README.md` — Versioning, Packing, Conventions, and Consuming these packages sections |
| `.github/workflows/**` | root `README.md` — Continuous integration and Publishing sections |
| `Graph/samples/Cyclotron.Graph.Sample/` | that sample's `README.md` |
| Test conventions or `TestSupport/` fakes | the package README only if it documents them; otherwise no doc change |

Doc buckets, outermost first: root `README.md` (repo mechanics — layout, versioning,
packing, CI, publishing, consuming) → `Graph/README.md` (family ground truth — package
split, dependency direction, which package to install) → `Graph/src/<PackageId>/README.md`
(per-package registration API, configuration, public surface) →
`Graph/samples/*/README.md` (how to run the harness). Facts belong at exactly one
level; when a fact must appear at two, both are edited together.

## Subagents

Uses the standard kit subagents: `evaluator` (plan evaluation, per-doc evaluation, final
audit) and `implementer` (per-doc editing). Standard retry rules apply (same subagent,
3 attempts, backoff — never substitute).

## Workflow

### Phase 6.0: Preconditions
Identify the shipped feature (explicit goal name, or the newest completed
`_goals/<goal>/goal.md`). Confirm the implementation actually landed (`git` shows the
code changes). If nothing shipped, stop and say so.

### Phase 6.1: Discover the change surface (READ-ONLY)
Read the goal + task files (read-only) and the diff
(`git diff --stat <base>...HEAD`, then targeted per-file diffs). Distill a **"what
shipped"** fact list — new/changed routes, modules, CLIs, config keys, UI surfaces,
auth behavior, renames, removals — each fact with its source file.

### Phase 6.2: Map changes to affected docs
Using the surface map above, list in-scope docs that plausibly mention each shipped
fact, then **grep those docs** for stale claims (old names, removed flags, "TODO",
"planned", "not yet"). Produce a **per-doc edit list** (or "no change — already
accurate"). Confirm `_research/` and `_goals/` are excluded.

### Phase 6.3: Evaluate the edit plan (`evaluator`)
Criteria: coverage (every stale doc caught?), scope (zero excluded-path or code edits
planned?), anti-invention (every planned edit cites its source file?). Mandatory
re-evaluate loop: PASS → 6.4; NEEDS REVISION → revise + re-evaluate (3 max); REJECT →
escalate.

### Phase 6.4: Execute per doc (`implementer` + `evaluator`)
Docs are independent — executors may run in parallel. Context package per doc: its
edit-list entry, the relevant shipped facts, exact files to READ FIRST, and the full
guardrails. Evaluate each result: accuracy, completeness, scope (only this doc changed),
preserved prose intact, links resolve. Re-evaluate loop, 3 cycles max per doc.

### Phase 6.5: Final audit (`evaluator`)
The whole change set: every affected doc accurate and **mutually consistent**; no
stale claims remain; `git status` shows **only** documentation files changed.
APPROVED → lint markdown if a linter is configured, report which docs changed (and
which were already accurate); ISSUES → remediate + re-audit (3 max).

## Escalation

Escalate when: an evaluator fails to PASS/APPROVED after 3 cycles; any REJECT; no shipped
feature found; or a guardrail conflict (the only accurate fix would require a code
change — which this skill must not make).
