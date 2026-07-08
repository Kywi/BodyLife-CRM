---
name: bodylife-logical-commits
description: Use when Codex finishes BodyLife CRM implementation, documentation, infrastructure, test, or skill changes and needs to validate the work, split the dirty worktree into logical git commits, stage only owned changes, and write clear commit messages.
---

# BodyLife Logical Commits

## Overview

Use this skill at the end of a task, after the relevant checks have passed or been attempted, to turn completed work into small, reviewable commits. The goal is to preserve task intent, keep unrelated dirty changes out, and make commit history useful for future debugging.

## Workflow

1. Inspect the worktree with `git status --short` and the relevant diffs.
2. Identify which changes belong to the current task. Treat pre-existing dirty files as user-owned unless the current task clearly touched them.
3. Run the relevant validation before committing. Use `bodylife-quality-gates` and any domain-specific BodyLife skill that applied to the implementation. For docs-only, skill-only, or agent-instruction changes, run structural validation appropriate to the files.
4. If validation fails or cannot run, do not commit unless the user explicitly asks to commit anyway. Report the command and outcome.
5. Split the current task into logical commits. Each commit should be understandable on its own and should keep the repository buildable/testable when an app scaffold exists.
6. Stage intentionally with explicit paths or hunks. Prefer `git add <paths>` for clean file groups and `git add -p` when a file mixes current-task and unrelated changes.
7. Review staged content with `git diff --cached --stat` and `git diff --cached --check` before each commit.
8. Commit one logical group at a time, then re-run `git status --short` before staging the next group.

## Splitting Rules

- Keep implementation with the tests that prove it. Do not split behavior and its tests unless the user explicitly wants red/green commits.
- Keep EF model changes, migrations, and migration tests together unless the migration is a deliberate standalone maintenance step.
- Separate independent concerns: infrastructure, domain behavior, UI, reports/audit, operations, docs, skills, and graph artifacts.
- Keep generated `graphify-out/` updates separate from product/code changes when they are intentional and tracked. Leave pre-existing graph dirt unstaged when it is unrelated.
- Do not stage secrets, local machine artifacts, build outputs, temporary logs, or unrelated user edits.
- If a mixed file cannot be safely staged by hunk, stop and explain the blocker rather than sweeping in unrelated changes.

## Commit Message Format

Use Conventional Commits:

```text
<type>(<scope>): <imperative summary>

- <what changed>
- <validation: command(s) and result>
```

Keep the subject line at or under 72 characters when practical. Use imperative mood, no trailing period, and a scope that names the owning module or concern.

Common types:

- `feat`: new user-facing or domain capability.
- `fix`: bug fix or incorrect behavior correction.
- `refactor`: internal restructuring without behavior change.
- `test`: tests-only change.
- `docs`: documentation, ADR, AGENTS, or skill instruction change.
- `build`: project scaffolding, dependencies, packaging, migrations tooling.
- `ci`: CI workflow or pipeline change.
- `chore`: repo maintenance that is not user-facing.

Common BodyLife scopes:

- `clients`, `memberships`, `membership-types`, `visits`, `payments`, `freezes`, `nonworking-days`
- `reports`, `audit`, `users`, `shared`
- `infra`, `ui`, `tests`, `docs`, `skills`, `graphify`

## Examples

```text
docs(skills): add logical commit workflow

- Added a BodyLife skill for tested, logical git commits.
- Validation: quick_validate.py passed for the new skill.
```

```text
build(infra): scaffold ASP.NET Core foundation

- Added the web app, module folders, PostgreSQL configuration, and health check.
- Validation: dotnet build and dotnet test passed.
```

```text
feat(memberships): calculate inclusive end dates

- Implemented base end date derivation from issued membership snapshots.
- Validation: membership domain tests passed.
```
