# BodyLife CRM Codex orchestration

## Purpose

This repository uses a native Codex multi-agent workflow to reserve GPT-5.6 Sol Ultra for scope, architecture decisions, integration, and final acceptance. Cheaper models handle bounded exploration, implementation, verification, and review. This changes the development workflow only; it does not change application runtime behavior or product architecture.

## Model routing

| Agent | Model and effort | Typical work |
|---|---|---|
| Root orchestrator | GPT-5.6 Sol / ultra | Scope, ADR decisions, handoffs, integration, acceptance, commits |
| `bodylife_scout` | GPT-5.6 Luna / low | Graphify, file discovery, requirements and ADR evidence |
| `bodylife_verifier` | GPT-5.6 Luna / medium | Builds, tests, analyzers, migrations, browser checks, log summaries |
| `bodylife_worker` | GPT-5.6 Terra / medium | One bounded implementation task at a time |
| `bodylife_reviewer` | GPT-5.6 Terra / high | Independent correctness, domain, persistence, audit, and test review |

The root plus at most three child threads may be open. Spawn depth is one, so child agents cannot create more agents. Only one agent may write to the shared worktree at a time, and only the root may stage or commit.

The root enforces this with `scripts/write_lease.py`, which atomically creates `.git/codex-write-lease` and stores owner/writer tokens. It acquires the owner lease before the first workspace write, grants exactly one writer token at a time, and releases it only after writable children stop and final integration/commit completes. Every writable child checks both tokens before each write-capable command. If the lease already exists, a new task reads its status and checks active tasks instead of replacing it. A leftover lease is stale only after confirming its recorded owner and writer are no longer active.

## Automatic trigger

The `bodylife-codex-orchestrator` skill applies when work has multiple independent investigations, crosses module/data/UI/test boundaries, creates noisy validation output, combines implementation with review, or closes a milestone. Short answers, status checks, and tiny localized edits remain single-agent tasks.

For an explicit run, ask:

```text
Use $bodylife-codex-orchestrator to implement this BodyLife CRM task, keep one writer, wait for verification and review, and let Sol own final integration and the commit.
```

Before writing, the root checks for another active Codex task in the repository, inspects the worktree, and acquires the write lease. If another task is writing or holds the lease, orchestration waits to avoid conflicting edits.

## Workflow

1. Sol runs the required Graphify query, reads the latest implementation progress, selects matching BodyLife skills, and defines acceptance criteria.
2. Luna gathers bounded evidence without changing tracked files.
3. Sol acquires the owner lease and grants one writer token; one Terra worker receives the tokens with a decision-complete implementation handoff and runs focused checks.
4. After the writer stops, Sol revokes its token and grants a verifier token for writable validation while Terra performs an independent read-only review.
5. Sol resolves findings, performs final validation, updates progress and Graphify, stages explicit paths, creates the logical commit, revokes the final grant, and releases the owner lease.

After two failed worker attempts, an ADR conflict, unclear module ownership, or unresolved membership/payment/concurrency semantics, work returns to Sol instead of spawning more agents.

## Availability and fallback

The project config pins Sol Ultra and enables stable multi-agent support. Start a new Codex task after pulling configuration changes so project config, custom agents, and the skill are reloaded.

The local model catalog includes Luna. If the spawn surface supports selecting named custom agents, use the checked-in profiles. If it exposes only model/effort/message parameters, copy the role instructions into the handoff and use an explicit Terra low/medium fallback when Luna is not accepted. Do not silently move those roles to Sol. The root records the actual model and effort supplied to each spawn; a child's role name or self-report is not evidence that Luna actually ran.

Use the Codex Subagents panel or CLI `/agent` view to inspect active and completed child threads. The final task response should state which roles/models ran, validation results, whether fallback occurred, and the commit. Multi-agent execution can consume more total tokens; the objective is to spend Sol reasoning only where it adds the most value, not to guarantee a lower bill.

## Preflight and acceptance checks

Run these checks after changing Codex versions or orchestration files:

```bash
codex --strict-config doctor --summary
codex debug models --bundled
python3 "$CODEX_HOME/skills/.system/skill-creator/scripts/quick_validate.py" .codex/skills/bodylife-codex-orchestrator
git diff --check
```

Confirm that Sol supports `ultra`, Terra supports the configured efforts, Luna is present, project config loads, and the skill validates. Then exercise four bounded scenarios: a simple status request with no spawn, a read-only investigation, a current-diff review, and a simulated cross-module handoff. If the current spawn API lacks a custom-agent selector or Luna override, the test must use and report the explicit Terra compatibility path rather than claim that the named Luna profile ran.

The validator command requires the installed `skill-creator` system skill under `CODEX_HOME`.

Probe the lease helper with unique test tokens: acquire must succeed once, a second owner must be rejected, the active writer must pass `check`, a different writer must fail, release must fail before revoke, and revoke plus release must cleanly remove the lease. Do not run this probe while another task is writing.
