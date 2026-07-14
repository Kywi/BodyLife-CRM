# BodyLife CRM Agent Instructions

## Current State

- This repository is currently a documentation and planning baseline for BodyLife CRM v1. There is no application scaffold yet: no `src/`, `.sln`, `.csproj`, migrations, tests, or running web app are present.
- The accepted ADR package is complete in `docs/adr/` and is the governing architecture source. If a later request conflicts with an accepted ADR, require a new ADR or an explicit ADR update instead of silently changing direction.
- The selected application stack is fixed in `docs/technology-stack-decision.md`: ASP.NET Core 10 LTS + Razor Pages/MVC + htmx + EF Core/Npgsql + PostgreSQL.
- Hosting provider is still pending. Production readiness requires backup/restore evidence, including at least 30-day backup retention expectation and a restore rehearsal before production use.
- The next implementation step is Milestone 1 from `docs/implementation-roadmap.md`: project scaffold and infrastructure. After that, implement the reception-oriented vertical slice from `docs/vertical-slice-plan.md`.

## Source Of Truth

Use these documents before inventing implementation details:

- `docs/adr/README.md` and `docs/adr/001..014-*.md` for accepted decisions.
- `docs/architecture-baseline.md` for the concise implementation contract.
- `docs/domain-model.md` for business entities, invariants, lifecycle rules, and membership calculations.
- `docs/data-architecture.md` for source facts, derived state, schema direction, constraints, indexes, audit, backfill, and reporting data access.
- `docs/interaction-contracts.md` for command/query contracts, transaction boundaries, permissions, errors, recalculation, audit, and canonical rereads.
- `docs/ui-workflows.md` for reception dashboard, search, profile, warnings, quick actions, reports, tablet-first and phone-friendly behavior.
- `docs/ui-design-foundation.md` for shared reception UI layout, visual language, component patterns, warning semantics, tablet/phone consistency, and first-screen exemplars.
- `docs/operations-design.md` for audit vs logs, structured logging, metrics, backup/restore, paper fallback, and incident/recovery policy.
- `docs/technology-stack-decision.md` for stack choice and hosting constraints.
- `docs/vertical-slice-plan.md` for the first end-to-end proof scenario.
- `docs/implementation-roadmap.md` for milestone order and acceptance criteria.

## graphify

This project has a knowledge graph at `graphify-out/` with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:

- For codebase or project-knowledge questions, first run `graphify query "<question>"` when `graphify-out/graph.json` exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than `GRAPH_REPORT.md` or raw grep output.
- Dirty `graphify-out/` files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If `graphify-out/wiki/index.md` exists, use it for broad navigation instead of raw source browsing.
- Read `graphify-out/GRAPH_REPORT.md` only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current. After modifying project-knowledge docs, use the full `/graphify --update`/semantic-update flow when available; the plain CLI `graphify update .` is code-only and will warn about doc changes.

## Local Skills

Use the local BodyLife skills when the task matches their scope:

- `bodylife-aspnet-foundation`: Milestone 1 scaffold, ASP.NET Core/Razor/htmx/EF/Npgsql/PostgreSQL foundation, CI, analyzers, health checks, module folders, command/query conventions.
- `bodylife-command-workflows`: state-changing commands/actions, authorization, idempotency, transaction boundaries, recalculation hooks, business audit, command result/error taxonomy.
- `bodylife-membership-rules`: issued membership snapshots, inclusive date arithmetic, remaining/negative visits, first negative visit date, freeze/non-working day union rules, `membership_state_cache`, Memberships queries.
- `bodylife-postgres-data-access`: EF Core/Npgsql mappings, migrations, PostgreSQL constraints, partial unique indexes, row locks, report/search indexes, PostgreSQL-backed integration tests.
- `bodylife-reception-ui-htmx`: Razor Pages/MVC + htmx reception dashboard, live search, exact card match, profile, warnings, quick actions, duplicate-submit protection, tablet/phone UI.
- `bodylife-reports-audit-history`: daily cash/visits reports, ending-soon/low-remaining/negative/inactive reports, drill-down consistency, client history, audit timeline.
- `bodylife-operations-production-readiness`: structured logging, correlation IDs, PII masking, health checks, backup/restore, restore rehearsal, paper fallback, production hardening.
- `bodylife-quality-gates`: tests and acceptance gates for domain rules, commands, PostgreSQL, migrations, reports, audit, Playwright, restore readiness.
- `bodylife-logical-commits`: post-validation workflow for splitting completed task changes into logical commits and writing clear commit messages.
- Use the design/research skills (`research-architecture-options`, `choose-technology-stack`, `design-data-architecture`, `design-system-interactions`, `design-observability-operations`) only for new research or design work. Do not reopen already accepted decisions unless the user asks to revisit them.

## Architecture Guardrails

- Build one hosted internal web app for one gym. Do not add SaaS/multi-tenant plumbing, client portal, public API platform, native mobile app, offline-first sync, online payments, POS/bank integrations, full import, or complex accounting in v1.
- Use a modular monolith: one deploy, one main transactional PostgreSQL database, business-oriented modules, and explicit public commands/queries.
- Keep Razor Pages/MVC server-rendered UI as the default. Use htmx only for reception-critical islands: live search, compact results, membership state refresh, warnings, quick actions, loading state, and duplicate-submit protection.
- The first real screen/workflow is the reception dashboard, not a landing page and not generic CRUD.
- Every state-changing workflow must go through a server-side command/action with authorization, validation, idempotency where needed, transaction boundary, recalculation decision, business audit, and canonical reread.
- UI, controllers, templates, and reports must not own membership formulas or mutate business state directly.

## Module Map

Use these top-level business modules when scaffolding or organizing code:

- `Clients/Search`: client identity, current card assignment, phone/name/card normalization, last-four search, duplicate warnings.
- `MembershipTypes`: owner-managed catalog, active/inactive lifecycle, no hard delete, immutable issue-time snapshots for issued memberships.
- `Memberships`: issued memberships, recalculation, active status, remaining visits, negative balance, first negative visit date, effective end date, extension days, warnings.
- `Visits`: visit source facts, visit consumption, cancellations.
- `Payments`: cash payment source facts, corrections/cancellations, negative closure payment context where applicable.
- `Freezes`: individual freeze source ranges and cancellation/correction facts.
- `NonWorkingDays`: owner-only global non-working periods, affected membership scope, cancellation/correction.
- `Reports`: report queries and drill-downs over canonical source records and Memberships public state.
- `Audit`: append-only business audit, separate from technical logs.
- `Users/Roles`: Owner, named Admin, shared Reception/Admin account, sessions, device/session accountability, permission policies.

Shared code should stay narrow: IDs, Money, DateRange, actor/session context, request correlation id, command envelope/result, and error taxonomy. Do not create shared business-rule services that steal ownership from modules.

## Membership Rules

- Memberships is the only owner of active status, remaining visits, negative balance, first negative visit date, effective end date, extension days, and membership warnings.
- Issued memberships copy immutable MembershipType snapshot fields at issue time: type name, duration days, visits limit, and price.
- Base end date is inclusive: `base_end_date = start_date + duration_days - 1 day`.
- `effective_end_date` is derived. Never edit it directly in ordinary workflows.
- Remaining visits is signed and may be negative. Negative visits are core workflow, not a separate debt ledger in v1.
- Counted visits exclude canceled visits. Cancel/correct workflows preserve history and trigger recalculation.
- Freeze and non-working day ranges are inclusive. Extension days are counted as a union of unique calendar dates, not a naive sum when ranges overlap.
- `membership_state_cache` and `membership_extension_days` are rebuildable derived state, not source truth.

## Data And Persistence

- PostgreSQL behavior is part of the product confidence. Do not use SQLite or EF Core InMemory to prove persistence behavior.
- Model source facts as canonical tables and derived state as rebuildable caches owned by the relevant module.
- Use PostgreSQL constraints for invariants that must never be violated: foreign keys, not-null/check constraints, positive amounts, non-negative prices/visits, inclusive range checks, and partial unique indexes for current/active rows.
- Use EF Core migrations with Npgsql. Keep migrations reviewable, generate SQL for production review, and use explicit SQL where EF hides important PostgreSQL behavior.
- Use row locks or deliberate transaction isolation for concurrent card assignment, visit marking, negative closure, and correction workflows.
- Reports read canonical records and Memberships public state/read models. They must not duplicate formulas for remaining visits, negative balance, active status, or effective end date.

## Commands, Audit, And Logs

- Commands carry actor account, role/account type, session/device, request correlation id, idempotency key where needed, entry origin, occurred date/time, server-set recorded time, and reason/comment when required.
- Required entry origins are `normal`, `manual_backfill`, `paper_fallback`, and future `future_import`.
- Backdated and paper fallback entries must use normal domain commands with `occurred_at`, server `recorded_at`, entry origin, actor/session, reason/comment, validation, recalculation, and audit.
- Successful commands that change business state create append-only business audit entries. Queries do not create business audit by default.
- Business audit is not technical logging. Technical logs must use correlation ids, stable error classes, durations, outcomes, and PII/secret masking.
- Do not hard-delete business history through application workflows. Corrections/cancellations add explicit facts and audit entries.

## UI Rules

- Tablet is the primary reception target; phone layout must preserve every critical warning and action in a usable order.
- Follow `docs/ui-design-foundation.md` for shared layout, visual language, status colors, warning blocks, quick action groups, forms, and first-screen exemplars.
- Touch workflows must not depend on hover-only affordances.
- State-changing buttons must show busy/disabled states and prevent duplicate submits.
- Exact unique current card match may auto-open a client. Partial or non-unique matches must render selectable results.
- Warnings come from the server: duplicate identity, zero/negative/expired membership, ending soon, low remaining, changed-after-close, backfill/fallback labels, and permission restrictions.
- After every successful mutation, reread canonical state from the server. Do not leave optimistic UI values as business truth.

## Quality Gates

- Every milestone should finish in a deployable, testable state.
- Milestone 1 must include build/analyzer gates, local/dev/test PostgreSQL setup, baseline migration apply check, health check, structured logging foundation, module boundary checks, and a Playwright smoke harness.
- Domain tests must cover inclusive dates, snapshot immutability, remaining visits, negative visits, first negative date, freeze/non-working union days, canceled facts, backdated entries, and rebuildable derived state.
- Command tests must cover permissions, validation, idempotency, stale/concurrency behavior, transaction rollback, recalculation, business audit, and canonical reread targets.
- PostgreSQL integration tests must cover migrations, constraints, partial unique indexes, row locks, source facts vs derived cache rebuild, and report/search indexes.
- Report consistency tests must prove totals equal drill-down source rows and corrections/cancellations remain visible.
- UI E2E tests must include tablet and phone reception flows, warnings, duplicate-submit protection, daily report, and audit/history links.
- Operations readiness requires health checks, structured logs with correlation ids, PII masking review, backup/restore runbook, restore rehearsal, owner restore-check, and paper fallback reconciliation drill.

## Commit Workflow

- At the end of implementation tasks, after the relevant tests/checks pass or have been attempted, use `bodylife-logical-commits` to split current-task changes into logical git commits.
- Do not commit before validation. If validation fails or cannot run, report the exact status and do not commit unless the user explicitly asks to commit anyway.
- Commit only changes that belong to the current task. Keep pre-existing dirty files, unrelated graph artifacts, local outputs, and user edits out of the staged set.
- Prefer commits that pair implementation with directly related tests, migrations, and documentation. Separate independent concerns such as infrastructure, domain behavior, UI, reports/audit, docs/skills, and graph updates.
- Use the commit message format and BodyLife scopes from `bodylife-logical-commits`.

## Forbidden Shortcuts

- No direct database edits as a product workflow for correction, migration, paper fallback, or operational repair.
- No formulas in templates, controllers, frontend state, report SQL snippets, or duplicated helpers outside Memberships.
- No generic CRUD-first UI that bypasses command/query boundaries.
- No report-specific reinterpretation of membership state or cash/visit truth.
- No technical-log-only business history.
- No app-level backup/export panel as the primary backup mechanism in v1.
- No hard delete for MembershipType or other business facts that must remain explainable.
- No pretending a shared Reception/Admin account identifies a physical person when the system only knows the shared account/session.
