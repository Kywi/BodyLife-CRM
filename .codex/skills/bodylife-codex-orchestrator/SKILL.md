---
name: bodylife-codex-orchestrator
description: Coordinate nontrivial BodyLife CRM implementation, diagnosis, review, research, milestone, or cross-module work across cheaper Codex model tiers while keeping GPT-5.6 Sol Ultra responsible for decisions and integration. Use when a task has multiple independent investigations, crosses module/data/UI/test boundaries, produces noisy test or log output, needs implementation plus independent review, or benefits from parallel read-heavy work. Do not use for short answers, simple status checks, or tiny localized edits with one obvious validation step.
---

# BodyLife Codex Orchestrator

Keep the root Sol/Ultra agent focused on scope, decisions, integration, acceptance, and commits. Route bounded support work to the project custom agents without allowing parallel writers.

## Guard The Workspace

1. Check for another active Codex task in the same repository when thread tools are available. If another task is writing, wait or stop before editing.
2. Inspect `git status --short`. Treat all pre-existing changes as user-owned and never revert or stage them.
3. Before the root or any child performs a workspace write, use `scripts/write_lease.py acquire --owner <root-token>`. Use a unique safe root token and keep it in the orchestration record. If acquisition fails, inspect lease status and active tasks, then wait; never replace another owner's lease. Hold it through implementation, writable validation, integration, and commit. Use `grant` to assign exactly one writer token, require that writer to run `check` before every write-capable command, use `revoke` before transferring the grant, and use `release` only after all writers stop. If a task crashes, treat the lease as stale only after the user or root confirms the recorded owner/writer are inactive.
4. Keep at most three child agents active alongside the root. Never raise `agents.max_depth` above `1` or ask a child to delegate.

## Decide Whether To Delegate

Delegate when at least one condition holds:

- Two or more independent questions can be researched or verified concurrently.
- The change spans business behavior plus persistence, UI, audit, reports, or tests.
- Test runs, logs, or large-file inspection would pollute the root context.
- A bounded implementation benefits from an independent correctness review.
- A milestone or acceptance review needs evidence from several quality gates.

Stay single-agent for a short explanation, a direct status lookup, a one-line correction, or a tiny edit whose complete validation is obvious. Delegation consumes extra tokens; do not spawn agents merely because capacity exists.

## Route Work

| Role | Model | Use for | Never allow |
|---|---|---|---|
| `bodylife_scout` | Luna / low | Graphify, file discovery, ADR and requirements evidence | Writes, decisions, commits |
| `bodylife_verifier` | Luna / medium | Builds, tests, analyzers, migrations, browser checks, log triage | Tracked edits, fixes, commits |
| `bodylife_worker` | Terra / medium | One decision-complete implementation unit | Parallel writing, scope expansion, commits |
| `bodylife_reviewer` | Terra / high | Independent domain, PostgreSQL, audit, security, and test review | Writes, final acceptance, commits |

When the spawn surface supports a custom-agent selector, select the named role so its `.codex/agents/<role>.toml` configuration loads. When it exposes only task name, model, effort, and message, pass the model/effort explicitly and prepend the corresponding custom agent's developer instructions to the bounded task. On such a surface, use Terra with matching `low` or `medium` effort for Luna-targeted roles unless Luna is explicitly accepted. Record the compatibility fallback; do not silently fall back to Sol.

The root must record the actual model and effort passed to each spawn call. Do not infer runtime model usage from the role's configured target or from a child's self-report, because a fallback child can still describe the role as Luna.

## Run The Workflow

1. **Ground with Sol.** Run the required Graphify query, read the latest entry in `docs/implementation-progress.md`, select the applicable BodyLife skills, and identify the governing ADRs/contracts. Define scope, acceptance criteria, owned files, and validation before delegation.
2. **Gather evidence.** Spawn `bodylife_scout` for bounded read-heavy unknowns. Spawn `bodylife_verifier` in parallel only when an independent baseline or failure reproduction is useful. Wait for their reports and resolve contradictions in the root thread.
3. **Write once.** Acquire the write lease, grant a unique writer token, and give one `bodylife_worker` a decision-complete handoff containing the exact owner/writer tokens and check command. Include exact behavior, boundaries, relevant source documents/skills, allowed paths, edge cases, tests, and stop conditions. Do not run any other writer until it finishes.
4. **Verify after writing.** Stop the writer, revoke its token, and grant a new verifier token for focused or full gates while `bodylife_reviewer` performs an independent read-only review. Use fresh review context so the reviewer does not inherit the writer's conclusions.
5. **Integrate with Sol.** The root resolves findings, makes any final integration edits, runs the final required validation, updates progress and Graphify, invokes `bodylife-logical-commits`, stages only owned paths, and commits.
6. **Release safely.** Confirm every writable child has stopped, revoke the final writer token, inspect the final worktree, and release only the lease whose owner token matches this root task.

Use the helper from the repository root:

```bash
python3 .codex/skills/bodylife-codex-orchestrator/scripts/write_lease.py acquire --owner <root-token>
python3 .codex/skills/bodylife-codex-orchestrator/scripts/write_lease.py grant --owner <root-token> --writer <writer-token>
python3 .codex/skills/bodylife-codex-orchestrator/scripts/write_lease.py check --owner <root-token> --writer <writer-token>
python3 .codex/skills/bodylife-codex-orchestrator/scripts/write_lease.py revoke --owner <root-token> --writer <writer-token>
python3 .codex/skills/bodylife-codex-orchestrator/scripts/write_lease.py release --owner <root-token>
```

## Escalate To Sol

Stop delegating and return the issue to the root after two failed worker attempts, an accepted ADR conflict, unclear cross-module ownership, unresolved membership/payment/concurrency semantics, a destructive or approval-sensitive action, or disagreement between canonical source facts and derived state. Sol must decide whether to clarify with the user, update an ADR, or narrow the task.

## Report The Run

In the final response, identify the roles and actual spawned models/efforts from the root's dispatch record, whether the Luna fallback occurred, the validation performed, the final commit, and any remaining risk. Never claim cost savings: multi-agent work can use more total tokens even when it reserves Sol for high-value reasoning.
