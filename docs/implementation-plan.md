# Implementation Plan

Status: practical implementation plan for BodyLife CRM v1. This document is based on the accepted ADR package and the current documentation baseline. It does not start implementation and does not change architecture decisions.

## 1. Documentation Map

Graphify was used first because `graphify-out/graph.json` exists:

- `graphify query "BodyLife CRM implementation roadmap milestones modules domain rules tests quality gates"`
- `graphify query "Memberships Clients Visits Payments Reports Audit Reception UI PostgreSQL Razor Pages implementation dependencies"`
- `graphify explain/path` for roadmap, Membership derived state, and the Reports/Membership relationship.

Primary sources:

- `AGENTS.md`: local guardrails, module map, quality gates, forbidden shortcuts.
- `docs/adr/README.md` and `docs/adr/001..014-*.md`: accepted architecture decisions. Any conflict requires a new ADR or explicit ADR update.
- `docs/architecture-baseline.md`: concise implementation contract and non-negotiable rules.
- `docs/technology-stack-decision.md`: selected stack: ASP.NET Core 10 LTS + Razor Pages/MVC + htmx + EF Core/Npgsql + PostgreSQL.
- `docs/domain-model.md`: entities, invariants, lifecycle rules, Memberships formulas, edge cases, domain tests.
- `docs/data-architecture.md`: source facts, derived state, schema outline, PostgreSQL constraints/indexes, audit, backfill, reporting data access.
- `docs/interaction-contracts.md`: command/query contracts, transaction boundaries, permissions, errors, recalculation, audit, canonical rereads.
- `docs/ui-workflows.md`: reception dashboard, search, profile, warnings, quick actions, htmx and tablet/phone behavior.
- `docs/operations-design.md`: business audit vs technical logs, backup/restore, paper fallback, correction/support workflow, production readiness.
- `docs/vertical-slice-plan.md`: first architecture proof scenario.
- `docs/implementation-roadmap.md`: 12 milestone roadmap, dependencies, acceptance criteria, tests and risks.
- `docs/first-version-requirements.md`: original business scope and ready criteria.

## 2. Implementation Strategy

Build v1 as a production-shaped modular monolith from the first commit. Do not start with generic CRUD, a marketing landing page, a public API platform, a SPA, or a database-only prototype. The first real screen is the reception dashboard.

Every mutation must go through a server-side command/action with:

- actor account, role, session/device and request correlation id;
- authorization and validation;
- idempotency key where duplicate submit risk exists;
- one PostgreSQL transaction for source facts, recalculation, derived cache updates and audit;
- Memberships recalculation when the command affects membership state;
- append-only business audit for successful state changes;
- canonical reread target for UI after commit.

Recommended initial physical scaffold, unless an implementation ADR changes it:

- `src/BodyLife.Web`: ASP.NET Core host, Razor Pages/MVC, htmx endpoints/partials.
- `src/BodyLife.Application`: commands, queries, policies, command envelope/result/errors.
- `src/BodyLife.Domain`: value objects and domain rules, especially Memberships calculation.
- `src/BodyLife.Infrastructure`: EF Core/Npgsql, migrations, persistence, logging adapters.
- `tests/BodyLife.Domain.Tests`
- `tests/BodyLife.Application.Tests`
- `tests/BodyLife.Postgres.Tests`
- `tests/BodyLife.Ui.Playwright`

Module folders should match the accepted module map:

- `Clients/Search`
- `MembershipTypes`
- `Memberships`
- `Visits`
- `Payments`
- `Freezes`
- `NonWorkingDays`
- `Reports`
- `Audit`
- `Users/Roles`

Shared code should stay narrow: IDs, `Money`, `DateRange`, actor/session context, request correlation id, command envelope/result and error taxonomy. Do not introduce a shared business-rule service that steals ownership from modules.

## 3. Milestone / Phase Plan

| Phase | Goal | Main deliverables | Dependencies | Acceptance criteria | Tests |
|---|---|---|---|---|---|
| 1. Foundation | Create deployable ASP.NET/PostgreSQL skeleton. | `.sln`, web app, module folders, command/result/error conventions, health check, structured logging foundation, CI, local/test PostgreSQL. | Documentation/ADR baseline only. | App starts, DB connects, baseline migration applies, analyzers/tests run, no CRUD-first UI. | Build/analyzers, migration apply, health smoke, architecture/module tests, Playwright smoke. |
| 2. Users/Roles | Add accountable actor/session model. | `accounts`, `sessions`, Owner/Admin/shared account, policies, command actor context. | Phase 1. | Commands receive actor/session/correlation; shared account is labeled honestly. | Auth integration, policy tests, session tests, log masking checks. |
| 3. Clients/Search | Reception search foundation. | `clients`, `client_card_assignments`, normalization, duplicate warnings, `SearchClients`, profile shell. | 1, 2. | Exact unique current card auto-opens; ambiguous matches list; duplicate current card blocked. | Search tests, partial unique index tests, command/audit tests, tablet/phone search smoke. |
| 4. MembershipTypes | Owner catalog for future sales. | `membership_types`, Owner-only create/edit/deactivate, active issue selector. | 1, 2. | No hard delete; inactive hidden from ordinary issue; audit for changes. | Command validation/policy/audit tests, PostgreSQL checks. |
| 5. Memberships | Central membership state owner. | `issued_memberships`, snapshots, opening states, `membership_state_cache`, `membership_extension_days`, recalculation, `GetMembershipState`. | 1-4 plus open domain decisions. | Inclusive base end date; signed remaining visits; negative state; derived cache rebuildable; no formulas outside Memberships. | Domain math tests, rebuild tests, PostgreSQL constraints, architecture checks. |
| 6. Visits | Visit marking/cancellation. | `visits`, `visit_consumptions`, `visit_cancellations`, `MarkVisit`, `CancelVisit`, warnings/idempotency. | 2, 3, 5. | Visit consumes one membership visit; zero-to-negative works with acknowledgement; cancel preserves history and recalculates. | Command transaction tests, idempotency, row lock/concurrency, Playwright mark/cancel. |
| 7. Payments | Cash payments/corrections. | `payments`, corrections/cancellations, `CreatePayment`, `CorrectPayment`, issue-with-payment consistency. | 2, 3, 5, 6. | Cash totals come from canonical rows; correction preserves original; duplicate submit blocked. | Payment command tests, report consistency, amount/check constraints, UI payment/correction. |
| 8. Freezes/NonWorkingDays | Extension source workflows. | `freezes`, non-working periods/applications, preview/confirm, union extension days. | 2, 3, 5, 6. | Inclusive ranges; overlaps count union days; Owner-only non-working; recalculation atomic. | Domain overlap tests, preview/scope tests, range constraints, UI freeze/non-working. |
| 9. Reports | Owner/admin operational visibility. | Daily cash/visits, ending soon, low remaining, negative, inactive reports with drill-down. | 3, 5, 6, 7, 8. | Totals equal drill-down rows; reports read Memberships state, not formulas. | Report consistency tests, query/index tests, Playwright report drill-down. |
| 10. Audit/History UI | Explainable business history. | `business_audit_entries`, `GetClientHistory`, `GetAuditTimeline`, links from profile/report. | 2-9. | Every implemented mutation has append-only audit; history shows original plus correction/fallback labels. | Audit matrix tests, append-only tests, access tests, UI timeline tests. |
| 11. Backup/Fallback Readiness | Prove recoverability and outage workflow. | Backup config evidence, restore runbook, restore rehearsal, paper fallback batches/template. | 1-10 plus hosting choice. | 30-day retention expectation documented; restore rehearsal passes; fallback reconciliation works through commands. | Restore rehearsal, migration on restore, cache rebuild compare, fallback batch test. |
| 12. Production Hardening | Go-live gate. | Staging/prod deploy process, secrets/session hardening, observability, full regression, owner UAT. | 1-11. | Full suite passes; no audit/report/recalc blockers; owner signs off restore and workflows. | Full regression, Playwright tablet/phone, security smoke, performance smoke, observability smoke. |

## 4. Detailed Task Breakdown

### 4.1. Foundation and Project Scaffold

Goal: create a production-shaped starting point that later modules can safely extend.

Scope:

- One internal hosted web app.
- One main PostgreSQL transactional database.
- Razor Pages/MVC as the first UI surface.
- htmx only for reception-critical islands.
- EF Core/Npgsql and migrations from day one.

Likely files/modules:

- `BodyLife.sln`
- `src/BodyLife.Web`
- `src/BodyLife.Application`
- `src/BodyLife.Domain`
- `src/BodyLife.Infrastructure`
- `tests/*`
- `.github/workflows/*` or the repo's chosen CI location
- `docker-compose.yml` or testcontainers configuration

Tasks:

1. Create .NET 10 solution and projects.
2. Add analyzers, nullable, formatting and CI build/test gates.
3. Add PostgreSQL local/test setup.
4. Add EF Core/Npgsql and baseline DbContext/migration.
5. Create module folders for all accepted modules.
6. Add shared primitives: IDs, `Money`, `DateRange`, actor/session context, request correlation id.
7. Add command envelope, command result and stable error taxonomy.
8. Add idempotency storage foundation.
9. Add health check endpoint.
10. Add structured logging foundation with correlation id, route/command, duration, outcome and error class.
11. Add Playwright smoke harness.
12. Add architecture tests for module boundary direction.

Acceptance criteria:

- App starts locally and connects to PostgreSQL.
- Baseline migration applies against PostgreSQL.
- CI/local validation runs build, analyzers, tests, migration apply and Playwright smoke.
- Top-level module boundaries are visible.
- No generic CRUD-first UI bypasses commands/queries.

Risks:

- Scaffold becomes too generic and delays reception workflow.
- Technical folder layout replaces business ownership.
- Tests accidentally use SQLite/EF InMemory for PostgreSQL behavior.

Definition of done:

- Foundation is deployable, testable and ready for Users/Roles and Clients/Search.

### 4.2. Database Migrations

Goal: build source fact and derived state schema in dependency order, with PostgreSQL constraints proving product confidence.

Scope:

- Source facts as canonical tables.
- Derived caches as rebuildable state.
- Append-only business audit.
- Reviewable EF migrations plus explicit SQL where EF hides important PostgreSQL behavior.

Likely tables:

- `accounts`, `sessions`
- `clients`, `client_card_assignments`, `duplicate_warning_acknowledgements`
- `membership_types`
- `issued_memberships`, `membership_opening_states`, `membership_adjustments`
- `membership_state_cache`, `membership_extension_days`
- `visits`, `visit_consumptions`, `visit_cancellations`
- `payments`, `payment_corrections`, `payment_cancellations`
- `freezes`, `freeze_cancellations`
- `non_working_periods`, `non_working_period_applications`, `non_working_period_cancellations`
- `membership_negative_closures`, `membership_negative_closure_items`
- `day_reconciliations` if day close is accepted
- `entry_batches`
- `business_audit_entries`

Tasks:

1. Add migrations phase-by-phase, not one giant opaque migration.
2. Add FKs, not-null constraints, positive amount checks, non-negative price/visit checks and inclusive range checks.
3. Add partial unique index for current card assignment by card number.
4. Add partial unique index for one current card per client.
5. Add one active opening state per membership.
6. Add one cache row per membership.
7. Add indexes for search, recalculation, daily reports and audit timeline.
8. Generate SQL for review and deployment.
9. Add migration apply checks in CI.

Acceptance criteria:

- PostgreSQL constraints reject invalid facts.
- Partial unique indexes work under concurrent card assignment tests.
- Migrations are reviewable and apply cleanly to a fresh database.
- Derived cache rows are rebuildable from source facts.

Risks:

- EF abstractions hide SQL details for partial indexes and constraints.
- Direct schema shortcuts make future audit/report consistency hard.

Definition of done:

- Persistence foundation supports command implementation without direct DB patch workflows.

### 4.3. Domain and Business Rules

Goal: implement Memberships as the only owner of active status, remaining visits, negative balance, first negative date, effective end date, extension days and warnings.

Scope:

- Membership issue-time snapshots.
- Inclusive dates.
- Remaining and negative visits.
- Canceled visits.
- Freeze/non-working union extension.
- Opening states and backdated entries.
- Rebuildable derived state.

Tasks:

1. Implement immutable MembershipType snapshot copy on issue.
2. Implement `base_end_date = start_date + duration_days - 1 day`, unless explicitly changed before tests lock.
3. Implement signed `remaining_visits`.
4. Implement `negative_balance = max(0, -remaining_visits)`.
5. Implement first negative visit date from running counted visits.
6. Exclude canceled visits from counts, reports and last counted visit.
7. Implement freeze and non-working inclusive ranges.
8. Count extension days as a union of unique calendar dates.
9. Implement `membership_state_cache` rebuild comparison.
10. Prevent direct ordinary edits of `effective_end_date`.

Acceptance criteria:

- Domain tests cover every documented Memberships invariant.
- Reports/UI/Visits/Payments/Freezes/NonWorkingDays do not contain copied membership formulas.
- Recalculation failure blocks command success.

Risks:

- Multiple active memberships and visit assignment are unresolved.
- Negative closure behavior can hide old negative visits if not explicit.

Definition of done:

- Memberships can be trusted as the canonical state source for profile and reports.

### 4.4. Command Workflows

Goal: implement state-changing workflows through explicit commands with transaction, audit and canonical reread.

Command order:

1. `CreateClient`
2. `UpdateClient`
3. `AssignOrChangeCard`
4. `CreateMembershipType`
5. `EditMembershipType`
6. `DeactivateMembershipType`
7. `IssueMembership`
8. `MarkVisit`
9. `CancelVisit`
10. `CreatePayment`
11. `CorrectPayment`
12. `AddFreeze`
13. `CancelFreeze`
14. `AddNonWorkingDay`
15. `CorrectNonWorkingDay`

Each command must define:

- owning module;
- input and common envelope;
- permission policy;
- validation;
- idempotency behavior;
- transaction boundary;
- source facts changed;
- recalculation decision;
- audit event and summary;
- canonical reread target;
- stable errors.

Acceptance criteria:

- Source fact, recalculation and audit commit or roll back together.
- Duplicate submit does not duplicate visits, payments, freezes or corrections.
- Server-side permission is authoritative.
- UI result points to canonical reread.

Risks:

- Splitting source fact, recalculation and audit into separate commits.
- UI handlers mutating business state directly.

Definition of done:

- Commands are testable without UI and safe for htmx/Razor forms.

### 4.5. UI Workflows

Goal: make reception workflows fast and safe without moving business truth into frontend state.

Screens/workflows:

- Reception dashboard with account/session/device indicator.
- Search by card/name/phone/last4.
- Exact unique current card auto-open.
- Compact multiple results.
- Client profile with membership panel, warnings, history and actions.
- Issue membership with snapshot preview and negative handling warning.
- Mark visit with zero/negative/expired acknowledgement.
- Add payment.
- Add/cancel freeze.
- Correction forms with reason/comment.
- Daily report with drill-down.
- Audit/history timeline links.

Acceptance criteria:

- Tablet is primary acceptance target.
- Phone layout preserves every critical warning and action.
- Touch workflows do not depend on hover.
- State-changing buttons show busy/disabled state.
- UI rereads canonical state after success.
- UI does not compute Memberships formulas.

Risks:

- htmx partials leave stale membership values.
- Compact mobile layout hides negative/expired/backfill/permission warnings.

Definition of done:

- Playwright tablet and phone flows pass for the implemented workflow.

### 4.6. Reports and Audit

Goal: build owner trust through totals that reconcile to source records and history that explains corrections.

Reports:

- `GenerateDailyReport`
- `ListEndingSoonMemberships`
- `ListLowRemainingMemberships`
- `ListNegativeClients`
- `ListInactiveClients`

Audit/history:

- `business_audit_entries`
- `GetClientHistory`
- `GetAuditTimeline`

Acceptance criteria:

- Daily visit count excludes canceled visits and equals drill-down rows.
- Daily payment count/cash sum reflects active canonical payments and corrections.
- Ending-soon, low-remaining and negative reports read Memberships state.
- Corrections after close, if day close exists, are labeled and explainable.
- Audit is append-only and separate from technical logs.

Risks:

- Reports recompute Memberships formulas for convenience.
- Audit becomes either too noisy or incomplete.

Definition of done:

- Profile, reports and audit explain the same source facts.

### 4.7. Integration and E2E Tests

Goal: prove behavior at the risk boundaries, not only through happy-path unit tests.

Required test groups:

- Domain tests for Memberships math and edge cases.
- Application command tests for permissions, validation, idempotency, concurrency, rollback, recalculation and audit.
- PostgreSQL integration tests for migrations, constraints, partial indexes, row locks, source facts vs derived cache rebuild.
- Report consistency tests for totals and drill-downs.
- Audit matrix tests for required fields and append-only policy.
- Playwright E2E for tablet/phone reception workflows.
- Operations tests/rehearsals for restore and fallback readiness.

Definition of done:

- Each phase extends the relevant gate before the feature is considered accepted.

### 4.8. Production Readiness

Goal: be safe to use as the business system of record.

Tasks:

1. Choose hosting only after validating backup/restore capabilities.
2. Configure automated backups with at least 30-day retention expectation.
3. Document RPO/RTO. RPO should be several hours/PITR if possible and not worse than 24 hours.
4. Add restore runbook matching actual deployment and migration process.
5. Run restore rehearsal into isolated staging/test.
6. Run owner restore-check: login, search client, profile state, daily report, audit/history.
7. Add structured logs, health checks and operational notification path.
8. Add paper fallback template and entry batch workflow.
9. Reconcile fallback entries through normal commands, daily reports and audit.

Acceptance criteria:

- Restore rehearsal passes before production use.
- Owner accepts restore-check.
- Paper fallback can be reconciled without direct DB edits.
- No app-level backup/export panel is treated as primary backup.

Definition of done:

- Production go-live checklist is signed off by owner/developer.

## 5. Quality Gates

After every phase:

- Build, formatting/analyzers and unit tests pass.
- PostgreSQL-backed migration/integration tests pass for persistence changes.
- No new direct DB-edit product workflow exists.
- No Memberships formula is added to UI, controllers, templates, Reports or ad hoc SQL snippets.
- New mutations have authorization, validation, idempotency where needed, transaction boundary, audit and canonical reread.
- New reports have drill-down equality tests.
- New UI workflows have tablet/phone Playwright coverage when user-facing.

Before production:

- Full domain/application/PostgreSQL/report/audit/Playwright regression passes.
- Restore rehearsal passes.
- Owner restore-check passes.
- Structured logs include correlation id, route/command, duration, outcome and error class.
- PII/secrets logging policy is reviewed.
- Backup retention, migration procedure, support workflow and paper fallback are documented.
- Out-of-scope features are absent: client portal, public API, online payments, offline sync, multi-tenant/SaaS, full import, complex accounting/POS.

## 6. Risks And Open Questions

Risks:

- Formula drift if Reports or UI shortcut Memberships state.
- EF Core migration opacity around PostgreSQL partial indexes, checks and row locks.
- NonWorkingDay mass recalculation can become slow or partially applied.
- Shared Reception/Admin account can be misunderstood as physical-person accountability.
- Backup provider can look acceptable until restore rehearsal fails.
- Scope creep into client portal, import, POS, online payments, SaaS, or generic admin CRUD.
- Backdated/fallback entries can erode trust if `occurred_at`, `recorded_at`, `entry_origin` and reason are not visible.
- Recalculation failure is high-risk and must fail the command rather than create partial success.

Open questions from the docs:

ADR-005 resolves inclusive date arithmetic. ADR-014 resolves multiple
Memberships, Visit selection/no-active behavior, one-off/trial context,
same-date ordering and Visit-during-Freeze blocking.

1. Define whether NonWorkingDay extends only overlapping active calendar days or the full period once any overlap exists.
2. Define Freeze validation outside membership active range.
3. Specify one-off negative closure behavior.
4. Define which correction/cancellation actions always require reason/comment.
5. Define day close/reconciliation command and changed-after-close policy if needed.
6. Choose default inactive-client threshold while keeping 14/30/60 available.
7. Decide whether denied permission attempts are business-audited or only technically logged.
8. Decide how much historical card-assignment history is visible beyond current card number and audit trail.
9. Choose hosting provider and backup/PITR plan.

## 7. First Execution Sprint

Start with Milestone 1 only. Do not implement business workflows until the foundation is in place.

Recommended first 10 development tasks:

1. Create a `codex/...` branch and scaffold `BodyLife.sln` with web/application/domain/infrastructure/test projects.
2. Add .NET 10 SDK pinning, analyzers, formatting, nullable warnings and CI build/test skeleton.
3. Add local/test PostgreSQL via Docker/Testcontainers and EF Core/Npgsql packages.
4. Create module folders for `Clients`, `MembershipTypes`, `Memberships`, `Visits`, `Payments`, `Freezes`, `NonWorkingDays`, `Reports`, `Audit`, `Users`.
5. Add shared primitives: IDs, `Money`, `DateRange`, actor/session context, correlation id, command envelope/result/errors.
6. Add baseline `DbContext`, first migration, migration apply check and SQL review script target.
7. Add health check endpoint and structured logging middleware with correlation id.
8. Add architecture tests for module boundaries and forbidden formula placement.
9. Add Playwright smoke harness that opens the app and verifies the future reception route placeholder.
10. Verify with build, analyzers, unit tests, PostgreSQL migration apply, health check smoke and Playwright smoke.

Sprint acceptance:

- The repo has an application scaffold and test harness.
- The app can start locally.
- PostgreSQL is used for integration/migration checks.
- CI/local checks are defined.
- Module boundaries are visible before business code begins.
- The next agent can start Milestone 2: Users/Roles, then Milestone 3: Clients/Search.
