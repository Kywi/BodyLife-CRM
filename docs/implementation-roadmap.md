# BodyLife CRM v1 implementation roadmap

Дата: 2026-07-07
Статус: implementation planning draft після vertical slice

Основа: `docs/architecture-baseline.md`, `docs/domain-model.md`, `docs/data-architecture.md`, `docs/interaction-contracts.md`, `docs/ui-workflows.md`, `docs/ui-design-foundation.md`, `docs/operations-design.md`, `docs/technology-stack-decision.md`, `docs/vertical-slice-plan.md` і accepted ADR package у `docs/adr/`.

Цей roadmap описує порядок повної реалізації BodyLife CRM v1 після успішного vertical slice. Він не замінює ADR і не додає новий scope. Якщо під час реалізації виникає конфлікт, перемагають accepted ADR і post-ADR implementation contract.

## Передумови

- Vertical slice завершений і показав, що обраний stack, modular monolith boundaries, server-side commands, Memberships recalculation, audit і report consistency працюють разом.
- Висновки зі slice перенесені в implementation notes: що лишається без змін, що треба виправити до production build lock.
- Відкриті продуктові питання з `docs/domain-model.md` і `docs/interaction-contracts.md` або закриті рішеннями, або явно винесені в milestone risks.
- V1 scope лишається internal hosted web app для одного залу, без client portal, online payments, offline-first sync, SaaS/multi-tenant model, full import або complex accounting.

## Dependency Map

| Milestone | Depends on | Unlocks |
|---|---|---|
| 1. Project scaffold and infrastructure | Successful vertical slice decision | All implementation work |
| 2. Auth/users/roles | 1 | Permissioned commands, honest audit/session context |
| 3. Clients and search | 1, 2 | Reception workflow, client profile, membership issuing |
| 4. Membership types | 1, 2 | Membership issue snapshots |
| 5. Memberships and recalculation | 1, 2, 3, 4 | Visits, payments, freezes, reports |
| 6. Visits and cancellations | 2, 3, 5 | Daily visits, negative state, inactive reports |
| 7. Payments and corrections | 2, 3, 5 | Daily cash, payment history, corrections |
| 8. Freezes and non-working days | 2, 3, 5, 6 | Extension rules, ending-soon reports |
| 9. Reports | 3, 5, 6, 7, 8 | Owner/admin operational visibility |
| 10. Business audit/history UI | 2 through 9 | Support, dispute review, correction explanation |
| 11. Backup/restore/paper fallback readiness | 1 through 10 | Production readiness evidence |
| 12. Production hardening | 1 through 11 | Go-live |

## Cross-Cutting Rules

- Every state-changing workflow is a server-side command/action with authorization, validation, idempotency where needed, transaction boundary, recalculation decision, audit entry and canonical reread.
- Memberships is the only owner of membership formulas: remaining visits, negative balance, first negative visit date, extension days, effective end date and warnings.
- Reports and UI read canonical state; they do not calculate membership truth locally.
- UI follows `docs/ui-design-foundation.md` for shared layout/components, warning visibility, tablet-first and phone-friendly consistency.
- Corrections and cancellations preserve source history and append audit. They do not hard-delete or silently patch business records.
- Backdated/manual/paper fallback entries use normal domain commands with `occurred_at`, server `recorded_at`, `entry_origin`, actor/session and reason/comment.
- PostgreSQL-backed tests are required for constraints, migrations, transactions, row locks, report queries and restore checks.
- Each milestone should leave the app in a deployable, testable state even when later business workflows are not implemented yet.

## Milestone 1. Project scaffold and infrastructure

### Ціль

Створити production-shaped foundation для full v1: solution/app skeleton, modular monolith boundaries, PostgreSQL persistence, migrations, CI, test harness, local/staging parity and basic technical operations. Після цього milestone команда має безпечно додавати бізнесові модулі без змішування правил у controllers/templates.

### Залежності

- Успішний або умовно прийнятий vertical slice.
- Підтверджений stack decision: ASP.NET Core 10 LTS, Razor Pages/MVC + htmx, EF Core/Npgsql, PostgreSQL.
- Узгоджені package/runtime versions and local development assumptions.

### Задачі

- Створити application skeleton для одного hosted internal web app і одного PostgreSQL database.
- Зафіксувати top-level module folders/names відповідно до architecture baseline: Clients/Search, MembershipTypes, Memberships, Visits, Payments, Freezes, NonWorkingDays, Reports, Audit, Users/Roles.
- Визначити shared primitives тільки для дозволених value objects: IDs, Money, DateRange, actor/session context, request correlation id.
- Додати command/query application layer conventions: common command envelope, common command result, common error taxonomy.
- Підняти PostgreSQL local/dev/test setup and first empty/baseline migrations.
- Додати migration workflow: generation, reviewable SQL, apply in CI/staging, forward-only/destructive migration policy.
- Налаштувати test projects/categories: domain, application command, PostgreSQL integration, migration, report consistency, Playwright UI.
- Додати formatting, analyzers, linting and CI gates.
- Додати basic structured logging foundation with `request_correlation_id`, environment, route/command, duration, outcome and error class.
- Додати health check endpoint/page for deployment monitoring.
- Додати idempotency key storage foundation, але без прив'язки до всіх бізнесових commands.
- Додати shared UI foundation assets/patterns for Razor layout, CSS tokens, warning blocks, action buttons and Playwright viewport smoke entry points, without building generic CRUD.
- Додати minimal seed/bootstrap path for initial Owner/named Admin/shared Reception/Admin accounts, якщо це не робиться в Milestone 2.

### Acceptance Criteria

- App запускається локально з PostgreSQL, а не з SQLite/EF InMemory для integration сценаріїв.
- CI запускає build, formatting/analyzers, unit tests, PostgreSQL-backed integration tests and migration apply check.
- Baseline migration створює технічний мінімум без business shortcut tables.
- Кожен top-level module має явну ownership boundary і не має direct cross-module writes.
- Common command envelope/result/error contract documented or represented in application layer conventions.
- Structured logs include correlation id and command/route outcome for at least a smoke request.
- Health check works in local/staging mode.
- Shared UI foundation exists for reception shell, warnings, buttons/forms and tablet/phone smoke rendering.
- Немає generic CRUD-first UI, який обходить command/query boundary.

### Потрібні тести

- Build and analyzer gate.
- Baseline migration apply/rollback policy check against PostgreSQL.
- Smoke integration test: app starts, DB connects, health check returns healthy.
- Architecture tests or review checks for module dependency direction.
- Testcontainers/docker-backed PostgreSQL test setup validation.
- Playwright smoke harness starts, even if business UI is still minimal.

### Ризики

- Scaffold може стати занадто generic і відтягнути reception workflow.
- ORM defaults можуть приховати PostgreSQL-specific constraints, які потрібні data architecture.
- Модулі можуть одразу перетворитися на technical folders замість business ownership.
- Early seed/setup може accidentally hard-code production credentials або roles.

### Що не входить

- Повні business workflows.
- Generic admin CRUD для всіх таблиць.
- Client portal, public API, offline sync, multi-tenant plumbing.
- Production hosting commitment, backup rehearsal or full observability vendor setup.

## Milestone 2. Auth/users/roles

### Ціль

Зробити accountable access model для v1: Owner, named Admin і shared Reception/Admin account, server-side permission policies, session/device context and honest audit identity. Після milestone жодна state-changing дія не повинна існувати без actor context.

### Залежності

- Milestone 1.
- Узгоджена bootstrap procedure для першого Owner account.
- Визначений мінімальний login/session model для internal hosted app.

### Задачі

- Реалізувати `accounts`, `sessions` and role/account-type persistence.
- Додати Owner, named Admin and shared Reception/Admin account lifecycle.
- Реалізувати login/logout/session tracking and device/session label where available.
- Додати server-side authorization policies for Owner-only, Admin+Owner, current/open-day correction and shared account behavior.
- Прокинути actor/session context у common command envelope.
- Додати UI indicator for current account/session/device on reception/admin screens.
- Додати permission result у queries so UI can show allowed actions, while server remains source of enforcement.
- Додати technical logs for auth failures and permission denials with sensitive-data masking.
- Зафіксувати policy for denied permission attempts: technical log only unless future owner policy requires business audit.

### Acceptance Criteria

- Owner can authenticate and manage/activate named Admin/shared Reception/Admin accounts according to v1 policy.
- Shared Reception/Admin actions identify the shared account and session/device, not an unknown physical person.
- Owner-only commands are rejected server-side for Admin/shared accounts.
- Admin+Owner reception commands receive valid actor/session context.
- UI displays current account/session in reception/admin surfaces.
- Permission-denied results do not mutate business state and are visible to the user.
- Technical logs for auth/permission events avoid passwords, tokens and unnecessary personal data.

### Потрібні тести

- Authentication integration tests for Owner, named Admin, shared Reception/Admin and inactive accounts.
- Authorization tests for Owner-only, Admin+Owner and closed-day owner policy placeholders.
- Session persistence/expiry tests.
- Command envelope tests proving actor/session/correlation id are available to application commands.
- UI smoke tests for current account/session display.
- Logging tests or review checks for secret/token masking.

### Ризики

- Shared account може створити false accountability, якщо UI/audit не показують shared identity чесно.
- UI-only permission hiding може дати bypass, якщо server policies неповні.
- Password/session implementation може роздути scope beyond internal app needs.
- Owner bootstrap може бути небезпечним, якщо лишити default credentials.

### Що не входить

- Client accounts, client portal або public self-service auth.
- Multi-tenant user model.
- Fine-grained staff HR/accountability beyond accepted Owner/Admin/shared account model.
- Full security compliance program або advanced IAM integration unless required by deployment.

## Milestone 3. Clients and search

### Ціль

Реалізувати client identity and reception search foundation: clients, current/historical card assignments, normalized phone/name/last4 search, duplicate warnings and profile shell. Після milestone рецепція може знайти правильного клієнта без generic CRUD.

### Залежності

- Milestone 1.
- Milestone 2 for actor/session, permissions and audit fields.

### Задачі

- Створити schema для `clients`, `client_card_assignments`, optional duplicate warning acknowledgements and search normalized fields/indexes.
- Реалізувати phone/card/name normalization rules and last-four phone extraction.
- Реалізувати `CreateClient`, `UpdateClient`, `AssignOrChangeCard` commands.
- Реалізувати current card uniqueness constraints: one current card per client and one current client per card number.
- Реалізувати duplicate warning flow for duplicate phone/similar name with explicit acknowledgement.
- Реалізувати `SearchClients` query with exact card priority, partial/ambiguous result list and no auto-open for non-unique matches.
- Реалізувати client profile shell from `GetClientProfile` with identity, current card, operational status, empty/current membership area placeholder and allowed actions from server.
- Додати audit entries for client create/update and card assign/change/clear.
- Додати tablet-first and phone-friendly UI states for search results, exact match, no match and multiple results using `docs/ui-design-foundation.md` patterns.

### Acceptance Criteria

- Client can exist without card number.
- Exact unique current card match returns/open auto-open target; partial/non-unique matches never auto-open.
- Duplicate current card assignment is blocked by DB constraint and command validation.
- Phone/name duplicate warning requires explicit acknowledgement before create/update continues.
- Card change/reassignment is separate from client update and requires reason when replacing/clearing existing card.
- Client profile shell rereads canonical server state and shows server-provided allowed actions.
- Audit entries exist for client create/update and card assignment changes with actor/session and before/after summary.
- Search works by card, name, normalized phone and last four phone digits.

### Потрібні тести

- Domain/application tests for card uniqueness, client without card and duplicate warning acknowledgement.
- PostgreSQL tests for partial unique current-card indexes and concurrent card assignment conflict.
- Search query tests for exact card, partial card, name, phone, last4 and inactive clients.
- Command tests for CreateClient, UpdateClient, AssignOrChangeCard permissions, idempotency where applicable, audit and validation errors.
- UI Playwright smoke: search by exact card, multiple results, open profile, no auto-open for ambiguous result.
- Accessibility/touch smoke for tablet and phone layouts.

### Ризики

- Search може стати fuzzy/import-like project замість достатнього v1 reception search.
- Duplicate data неминуче буде існувати, але merge clients не входить у v1.
- Card reassignment concurrency can violate trust if DB constraints are weak.
- UI може сховати важливі warnings у compact mobile/tablet layout.

### Що не входить

- Merge clients workflow.
- QR/NFC/turnstile/scanner-specific identity model.
- Full import or duplicate cleanup tooling.
- Client-facing profile or account.
- Complex fuzzy search unless simple normalized/prefix search proves insufficient.

## Milestone 4. Membership types

### Ціль

Реалізувати Owner-managed MembershipType catalog for future sales with immutable issue-time snapshot contract. Після milestone Owner може керувати типами абонементів, але зміни catalog ніколи тихо не змінюють already issued memberships.

### Залежності

- Milestone 1.
- Milestone 2 for Owner-only policies.
- Milestone 3 is useful for profile integration, but catalog can be built in parallel after auth.

### Задачі

- Створити `membership_types` schema with name, duration_days, visits_limit, price, active state and comment.
- Реалізувати `CreateMembershipType`, `EditMembershipType`, `DeactivateMembershipType`.
- Реалізувати Owner-only catalog/settings UI.
- Реалізувати `GetMembershipTypesForIssue` query: active types for ordinary issue flow, inactive visible only in owner/catalog/history contexts.
- Додати validation: duration > 0, visits_limit >= 0, price >= 0, no hard delete.
- Додати audit entries for create/edit/deactivate with before/after summaries and reason/comment for meaningful changes.
- Додати contract tests that issued membership snapshots will not read mutable catalog values once Milestone 5 exists.

### Acceptance Criteria

- Owner can create, edit and deactivate MembershipTypes.
- Admin/shared Reception cannot create/edit/deactivate MembershipTypes.
- Inactive types disappear from ordinary issue selector but remain readable in catalog/history/report contexts.
- No application workflow hard-deletes MembershipType.
- Catalog edit creates audit and does not affect already issued snapshot values.
- `GetMembershipTypesForIssue` returns only active types for ordinary issue flow.

### Потрібні тести

- Command tests for create/edit/deactivate validation, permission and audit.
- Query tests for active vs inactive visibility.
- PostgreSQL constraints for positive duration, non-negative visit limit and non-negative price.
- Snapshot contract test with Milestone 5 issued membership.
- UI tests for Owner catalog actions and Admin permission denial.

### Ризики

- Owner catalog UI може перетворитися на broad settings area before core workflows.
- Catalog edits may be accidentally used as mutable references by Memberships.
- Duplicate/similar type names need product policy; blocking too much can slow real work, allowing too much can confuse reception.

### Що не входить

- Hard delete.
- Complex product taxonomy, discounts, subscriptions, family/child modeling beyond separate MembershipTypes.
- Online sales, promo codes, POS or accounting integration.
- Automatic migration of existing issued memberships after catalog change.

## Milestone 5. Memberships and recalculation

### Ціль

Реалізувати canonical Memberships module: issued memberships, immutable snapshots, opening state/backfill source facts, central recalculation, `membership_state_cache`, extension explanation rows and public Memberships queries. Це core dependency for visits, payments, freezes, non-working days and reports.

### Залежності

- Milestone 1.
- Milestone 2 for actor/session/permissions.
- Milestone 3 for client ownership.
- Milestone 4 for MembershipType snapshots.
- Product decisions needed before completion: date arithmetic, multiple active memberships policy, visit without active membership default, one-off negative closure shape or explicit deferral.

### Задачі

- Створити `issued_memberships`, `membership_opening_states`, `membership_adjustments`, `membership_state_cache`, `membership_extension_days`.
- Реалізувати `IssueMembership` without silent negative hiding; payment integration can call Payments once Milestone 7 is available.
- Copy immutable MembershipType snapshot on issue: type name, duration, visits limit, price.
- Implement base end date formula: `start_date + duration_days - 1 day`, unless business confirms different convention before tests lock.
- Implement Memberships recalculation service for source facts available at this stage: issued membership, opening state, adjustments, future visit/payment/freeze/non-working inputs through public interfaces.
- Implement signed remaining visits, negative balance, first negative visit date, effective end date, extension days and warnings.
- Implement `GetMembershipState` and membership section of `GetClientProfile`.
- Implement `PreviewIssueMembership`, including negative balance warning and explicit negative handling decision requirement.
- Add rebuild command/service for `membership_state_cache` from source facts.
- Add guardrails so UI, Reports, Visits, Payments, Freezes and NonWorkingDays cannot own formulas.

### Acceptance Criteria

- Issued membership stores immutable snapshot values and keeps them after MembershipType edit.
- Base end date and active-by-date behavior follow the accepted inclusive date convention.
- `membership_state_cache` is derived and rebuildable from source facts.
- Memberships public query is the only source for remaining visits, negative balance, first negative date, effective end date, extension days and warnings.
- Direct effective-end-date edit is impossible through ordinary workflows.
- Issuing membership with existing negative state requires explicit decision and does not let a payment/new membership hide old negative visits silently.
- Client profile reads Memberships state through public query after command success.
- Recalculation failure causes command failure/rollback rather than partial success.

### Потрібні тести

- Domain tests for inclusive end date, active-by-date, snapshot immutability and direct end-date edit rejection.
- Domain tests for remaining visits from counted visits, negative balance, first negative date, cancellation recalculation hooks using synthetic/source fixtures.
- Application tests for `IssueMembership`, `PreviewIssueMembership`, opening state/manual backfill metadata and negative decision requirement.
- Rebuild tests comparing `membership_state_cache` with recalculated state.
- PostgreSQL tests for issued membership constraints, active opening state uniqueness and cache row uniqueness.
- Architecture tests/reviews to prevent formula duplication outside Memberships.

### Ризики

- Open domain questions can block stable recalculation, especially multiple active memberships and visit allocation.
- Formula drift can appear early if profile/report code calculates shortcuts.
- Opening state/backfill can become fake-history migration if boundaries are not enforced.
- Effective end date may be patched directly under pressure unless source-reason model is strict.

### Що не входить

- Visit recording/cancellation UI and persistence, except test fixtures/interfaces.
- Full Payments module and payment correction workflow.
- Freeze/NonWorkingDay commands, except recalculation extension interfaces/placeholders.
- Full report screens.
- Full Excel/paper import.

## Milestone 6. Visits and cancellations

### Ціль

Реалізувати Visit workflow: marking visits, visit consumptions, negative visit transition, cancellation with reason, idempotency and recalculation. Після milestone reception can record and correct visits while Memberships, profile and daily visit source rows agree.

### Залежності

- Milestone 2.
- Milestone 3.
- Milestone 5.
- Product decision for multiple active memberships and visit during active freeze policy.

### Задачі

- Створити `visits`, `visit_consumptions`, `visit_cancellations`.
- Реалізувати `MarkVisit` with selected membership or explicit one-off/trial context.
- Реалізувати zero/negative/expired warning acknowledgement rules.
- Реалізувати `CancelVisit` with reason/comment and changed-after-close marker support if day reconciliation exists.
- Lock affected membership/source rows and recalculate synchronously in the same transaction.
- Додати idempotency keys for visit quick actions and cancellation commands.
- Додати UI quick action on dashboard/profile, including warning acknowledgement and busy/disabled state.
- Додати visit rows to client history/profile.
- Prepare report-facing query/source shape for daily visit totals and cancellations.
- Add audit events: `visit.marked`, `visit.canceled` with before/after membership summary.

### Acceptance Criteria

- Marking a visit consumes exactly one active counted visit for the selected membership.
- Recording a visit at 0 remaining visits is allowed only with explicit warning acknowledgement and produces negative state.
- First negative visit date is recalculated by Memberships.
- Canceling a visit preserves visible history, deactivates counted consumption, recalculates membership state and excludes the visit from active visit totals.
- Duplicate tap/submit does not create duplicate visits.
- Visit command commits source fact, consumption, recalculation and audit together or rolls back together.
- Profile rereads canonical state after visit/cancellation success.
- Canceled visits do not count as last counted visit for inactive-client reporting.

### Потрібні тести

- Domain tests for remaining visits, zero-to-negative, multiple negative visits and first negative date.
- Domain/application tests for canceling normal visit and canceling first negative visit.
- Application command tests for permissions, warning acknowledgement, idempotency, concurrency conflict and rollback on recalculation/audit failure.
- PostgreSQL tests for at most one active counted consumption per visit and FK/client-membership consistency.
- Report-source tests for active vs canceled visit rows.
- Playwright tests: mark visit, acknowledge negative warning, cancel visit, verify profile refresh.

### Ризики

- Ambiguous membership selection can create wrong consumptions if multiple active memberships are allowed.
- Negative visits can hide product decisions around one-off/trial visits.
- Quick reception UI can double-submit without strong idempotency.
- Visit during freeze policy may be unresolved and produce inconsistent UX.

### Що не входить

- Payment creation/correction.
- Full daily report UI, beyond source data readiness.
- One-off/trial product polish unless explicitly chosen for v1.
- Turnstile/check-in automation, QR/NFC or self check-in.
- Full day close/reconciliation workflow unless a separate decision adds it.

## Milestone 7. Payments and corrections

### Ціль

Реалізувати v1 cash Payments: create payment, correction/cancellation by replacement facts, daily cash source truth, issue-membership payment integration and explicit negative-closure behavior if accepted for v1.

### Залежності

- Milestone 2.
- Milestone 3.
- Milestone 5.
- Milestone 6 for combined reception/profile/report consistency.
- Product decision for one-off negative closure and day close/reconciliation policy.

### Задачі

- Створити `payments`, `payment_cancellations`, `payment_corrections`.
- Реалізувати `CreatePayment` for cash-only contexts: membership sale, one-off/trial, negative closure or other accepted v1 context.
- Integrate optional cash payment into `IssueMembership` workflow without splitting transaction consistency.
- Реалізувати `CorrectPayment` with replace/cancel mode, reason/comment and old/new occurred date explainability.
- Enforce `amount > 0`, `method = cash`, valid client/membership relationship and entry origin metadata.
- Recalculate Memberships only when payment participates in issue/negative closure/correction policy.
- Update daily cash report source queries to read canonical payment status/replacement rows.
- Add idempotency for quick payment creation and correction/cancellation.
- Додати UI add payment flow and correction flow from profile/history/report drill-down.
- Add audit events: `payment.created`, `payment.corrected`, `payment.canceled`, and optional `membership_negative_closure.created`.

### Acceptance Criteria

- Cash payment appears in client history and daily cash source rows after commit.
- Ordinary standalone payment does not change membership negative state unless explicit negative closure policy is selected.
- Issue membership with payment commits issued membership, payment, recalculation and audit consistently.
- Correct/cancel payment preserves original fact and creates replacement/correction/cancellation facts.
- Corrected amount/date changes live daily cash totals through canonical records, not manual report patches.
- Corrections after closed/reconciled day are Owner-only or follow explicit owner-approved policy.
- Duplicate payment submit does not create duplicate cash rows.
- Audit includes before/after payment summary, reason/comment and changed-after-close marker where applicable.

### Потрібні тести

- Command tests for `CreatePayment` validation, permission, idempotency and audit.
- Command tests for `CorrectPayment` replace/cancel modes, reason requirement, day-close permission and old/new date explainability.
- Transaction tests for issue-membership-with-payment rollback on recalculation/audit/payment failure.
- Report consistency tests for daily cash count/sum before and after correction/cancellation.
- PostgreSQL tests for amount/check constraints and FK membership-client consistency.
- UI tests for add payment, duplicate-submit protection and payment correction.

### Ризики

- Payment existence may be incorrectly treated as closing negative visits.
- Payment replacement model can become confusing if original/replacement are not clear in history.
- Partial payment/accounting expectations can creep into v1.
- Cash day close policy is referenced by operations but not fully defined as a command.

### Що не входить

- Online payments, bank terminals, POS or accounting integration.
- Complex receivables/debt ledger.
- Partial-payment accounting beyond accepted v1 cash fact model.
- Exported invoices/receipts unless separately scoped.
- Full day close/reconciliation command unless added by decision.

## Milestone 8. Freezes and non-working days

### Ціль

Реалізувати extension source workflows: individual Freezes and global NonWorkingDays with Owner preview/confirmation, union calendar-day extension, corrections/cancellations, recalculation and explainable profile/history.

### Залежності

- Milestone 2.
- Milestone 3.
- Milestone 5.
- Milestone 6 for visit/state interactions.
- Product decisions for freeze range validation, visit during freeze and NonWorkingDay application scope.

### Задачі

- Створити `freezes`, `freeze_cancellations`, `non_working_periods`, `non_working_period_applications`, `non_working_period_cancellations`.
- Реалізувати `AddFreeze` and `CancelFreeze` with inclusive date range, reason/comment and synchronous membership recalculation.
- Реалізувати `PreviewNonWorkingDayImpact` with affected membership count/list, overlap warnings and expiring confirmation token.
- Реалізувати Owner-only `AddNonWorkingDay` with affected scope confirmation captured in application rows.
- Реалізувати Owner-only `CorrectNonWorkingDay` for replace range/reason or cancel, including old/new affected scope.
- Rebuild `membership_extension_days` from active freezes, non-working periods and adjustments, counting unique calendar dates.
- Додати profile/history extension explanation rows.
- Додати UI for add/cancel freeze and owner non-working day workflow.
- Add audit events: `freeze.added`, `freeze.canceled`, `non_working_day.added`, `non_working_day.corrected`, `non_working_day.canceled`.
- Define fallback behavior if mass recalculation becomes too slow; UI must not treat incomplete recalculation as success.

### Acceptance Criteria

- Freeze range is inclusive and changes effective end date only through Memberships recalculation.
- Canceling freeze preserves history and removes its active extension days.
- NonWorkingDay add/correction is Owner-only and requires preview/affected-scope confirmation.
- Freeze and NonWorkingDay overlap counts union calendar days, not sum of sources.
- NonWorkingDay application scope is stored and explainable.
- Correcting/canceling NonWorkingDay recalculates old and new affected memberships.
- Profile shows extension reasons and history for freeze/non-working sources.
- Recalculation and audit are committed consistently; failure blocks success.

### Потрібні тести

- Domain tests for inclusive freeze days, canceled freeze, NonWorkingDay days and overlap union.
- Application tests for Add/CancelFreeze permissions, validation, idempotency, audit and rollback.
- Application tests for Preview/Add/CorrectNonWorkingDay: owner-only, preview expiry, affected_scope_changed, overlap warning and recalculation.
- PostgreSQL tests for date range constraints and application rows.
- Performance/transaction tests for realistic affected membership counts.
- UI tests for add freeze, cancel freeze, non-working preview/confirm/correct.

### Ризики

- NonWorkingDay mass recalculation can be slow or partially applied.
- Preview can become stale between view and commit.
- Overlap rules can be accidentally double-counted.
- Freeze validation policy outside active membership dates may be unresolved.
- Admin may expect to manage NonWorkingDays, but ADR-012 makes it Owner-only.

### Що не входить

- Calendar integrations.
- Automatic holiday import.
- Double extension for exceptional days without explicit audited adjustment.
- Async job infrastructure unless v1 scale proves synchronous recalculation impossible.
- Per-client custom non-working rules beyond freezes/adjustments.

## Milestone 9. Reports

### Ціль

Реалізувати v1 report layer: daily cash/visits, ending soon, low remaining visits, negative clients and inactive clients with drill-down to canonical source records, Memberships state and audit/history explanations.

### Залежності

- Milestone 3.
- Milestone 5.
- Milestone 6.
- Milestone 7.
- Milestone 8.

### Задачі

- Реалізувати `GenerateDailyReport` with visit count, payment count, cash sum, visit/payment drill-down rows, correction/cancellation rows and changed-after-close labels.
- Реалізувати `ListEndingSoonMemberships` with `days_left <= 7` from Memberships effective end date.
- Реалізувати `ListLowRemainingMemberships` with `remaining_visits <= 2` from Memberships state.
- Реалізувати `ListNegativeClients` with negative balance and first negative visit date from Memberships state.
- Реалізувати `ListInactiveClients` for 14/30/60 day thresholds using last counted visit and excluding canceled visits.
- Додати report UI: date/threshold filters, drill-down links, profile/history navigation and permission-aware actions.
- Ensure reports read canonical source records and Memberships public state, not audit as source of totals.
- Add indexes/query review for daily reports, membership lists and inactive clients.
- Add optional day reconciliation display if the day close source fact exists; otherwise keep changed-after-close labels compatible with future command.

### Acceptance Criteria

- Daily visit count excludes canceled visits and equals active visit drill-down rows.
- Daily payment count/cash sum excludes canceled/replaced payments according to canonical payment status and equals payment drill-down rows.
- Corrected payment amount/date keeps old and new affected report dates explainable.
- Ending-soon, low-remaining and negative reports use Memberships state and do not duplicate formulas.
- Inactive report excludes canceled visits from last counted visit.
- Every report total has drill-down to source records and relevant audit/history.
- Client profile and report membership values agree for the same query date.
- Report query failure does not show partial totals as authoritative.

### Потрібні тести

- Report consistency tests for daily visits, daily cash, corrections, cancellations and drill-down row equality.
- Tests proving Reports do not compute remaining visits, negative balance or effective end date independently.
- Query tests for ending soon, low remaining, negative clients and inactive thresholds.
- PostgreSQL query/index tests for expected report paths.
- UI tests for daily report, drill-down links, correction launch from report and threshold lists.
- Regression tests for changed-after-close labels when applicable.

### Ризики

- Reports may duplicate Memberships formulas for convenience.
- Daily report can become accounting/finance module beyond v1.
- Long-period financial reporting/export expectations can creep in.
- Query performance can degrade if indexes are added after UI builds.
- Corrections after day close can surprise users without strong labels.

### Що не входить

- Long-period financial reports, accounting reports or tax reports.
- Exported report snapshots as source of truth.
- Data warehouse/reporting database.
- App-level backup/export UI.
- Client-facing report access.

## Milestone 10. Business audit/history UI

### Ціль

Зробити owner/admin-readable business history and audit UI across clients, reports and corrections. Audit має пояснювати успішні commands and corrections, але не бути джерелом report totals або membership formulas.

### Залежності

- Milestone 2.
- Milestones 3 through 9, because audit events must cover implemented commands.

### Задачі

- Finalize `business_audit_entries` schema and append-only policy hardening.
- Ensure every implemented state-changing command writes required audit fields: actor/account, role, session/device, action type, entity refs, related ids, `entry_origin`, `occurred_at`, `recorded_at`, before/after or domain summary, reason/comment, correlation id, idempotency key where applicable.
- Реалізувати `GetClientHistory` with memberships, visits, payments, freezes, non-working applications, opening states, negative closures, corrections and entry-origin labels.
- Реалізувати `GetAuditTimeline` by client/entity/date/action with owner/admin access scopes.
- Додати audit/history links from profile, report drill-down and correction forms.
- Додати owner-readable before/after summaries for corrections/cancellations and settings/catalog changes.
- Enforce no UPDATE/DELETE audit entries through application workflows.
- Add support investigation path using `request_correlation_id` to technical logs.
- Review audit noise: keep important business actions readable and avoid raw technical diffs as the primary owner view.

### Acceptance Criteria

- Owner/Admin can inspect client history and audit timeline without technical log noise.
- All state-changing commands implemented so far have audit entries matching the operations audit matrix.
- Corrections/cancellations show original fact plus correction/cancellation fact, not rewritten history.
- Backdated/manual/paper fallback entries display both `occurred_at` and `recorded_at` plus `entry_origin`.
- Shared Reception/Admin audit honestly shows shared account/session/device.
- Audit rows are append-only through application workflows.
- Reports/profile/history link to the same source facts and audit explanations.
- Technical logs can be correlated from audit via `request_correlation_id`, but logs are not used as business truth.

### Потрібні тести

- Audit matrix tests for all implemented commands.
- Append-only policy tests or DB permission/trigger tests where feasible.
- Access tests for Owner vs Admin audit/history visibility.
- UI tests for profile history, audit timeline, report drill-down to audit and correction explanation.
- Backdated/fallback display tests for `occurred_at`, `recorded_at` and `entry_origin`.
- Technical log correlation smoke test.

### Ризики

- Audit can become unreadable if every low-level implementation detail is shown.
- Missing before/after summaries can make owner disputes impossible to resolve.
- Treating audit as report source can create formula drift.
- Shared account/session labeling can be misunderstood as physical-person accountability.
- Personal data in audit needs careful role-controlled access.

### Що не входить

- Technical log viewer as a product feature.
- Business audit mutation/redaction workflow beyond future legal/privacy procedure.
- Report-access auditing for read-only queries unless future owner policy requires it.
- Full support ticketing system.

## Milestone 11. Backup/restore/paper fallback readiness

### Ціль

Довести, що production data can be recovered and outage work can be reconciled without direct DB edits: provider backups, restore runbook, restore rehearsal, owner restore-check, paper fallback template and backdated/fallback entry workflow.

### Залежності

- Milestone 1 for deployment/migration foundation.
- Milestones 2 through 10 for business data, audit, reports and correction workflows.
- Chosen hosting/provider backup capabilities.

### Задачі

- Configure provider-managed automated backups for full backup scope: database, migration version, app configuration needed for restore and uploaded files if introduced.
- Confirm minimum 30-day retention and RPO not worse than 24 hours; prefer PITR/several-hour RPO if provider supports it.
- Write restore runbook matching actual deployment and migration process.
- Execute restore rehearsal into isolated staging/test environment.
- Run restore-check procedure with owner: login, search known client, profile state, daily report, audit/history, fallback/backfill labels if present.
- Record evidence: snapshot timestamp, rehearsal time, operator, restored environment, schema version, observed RPO/RTO, owner result and follow-ups.
- Add rebuild/consistency checks after restore: current card uniqueness, membership state cache rebuild comparison, daily cash sample, recent audit rows.
- Create paper fallback template with batch id, line number, client/card, event type, `occurred_at`, payment/range/source and reason/comment fields.
- Implement or finalize `entry_batches` for `manual_backfill` and `paper_fallback`.
- Ensure fallback/backdated visits/payments/freezes/memberships use normal commands with `entry_origin`, reason/comment, validation, recalculation and audit.
- Document reconciliation process: enter paper rows, generate daily reports, compare cash/visit totals, inspect drill-down/audit, correct mismatches only via commands.

### Acceptance Criteria

- Automated backups are enabled and documented for the production-like environment.
- Restore runbook matches the actual hosting/database/migration setup.
- At least one pre-production restore rehearsal passes in isolated environment.
- Owner completes restore-check and blocking discrepancies are fixed or rehearsal repeated.
- Rebuilt membership state matches stored `membership_state_cache` on restored copy or drift is explained and fixed.
- Paper fallback template is ready and understandable by reception/admin staff.
- Paper fallback entries can be entered with `entry_origin = paper_fallback`, actual `occurred_at`, server `recorded_at`, actor/session and paper batch reference.
- Reconciliation can prove paper rows, daily report totals, cash totals and audit entries agree.
- No app-level export UI or developer-only manual dump is treated as primary backup.

### Потрібні тести

- Restore rehearsal as operational test.
- Migration apply check on restored database.
- Membership cache rebuild comparison on restored copy.
- Report sample checks after restore: daily cash, visits, corrections/cancellations and drill-down.
- Audit sample checks after restore for recent commands and fallback/backfill entries.
- Application tests for `entry_origin`, entry batches, paper fallback reason/comment and backdated recalculation.
- UI/operations drill: enter a small fallback batch and reconcile it through reports/history.

### Ризики

- Provider backup settings can look enabled but fail restore needs without rehearsal.
- Human paper fallback discipline can fail without clear batch/line fields.
- Backdated entries can erode trust if `occurred_at`, `recorded_at`, origin and reason are not visible.
- Restore can lose post-snapshot business actions; recovery must use paper/recovery fallback entries, not direct DB patches.
- No app-level export UI means operational runbook must be reliable.

### Що не входить

- App-level backup/export/admin backup panel.
- Full Excel/paper historical import.
- Developer manual dump as primary backup.
- Restoring whole database to fix one mistaken visit/payment/freeze.
- Long-term legal retention/redaction policy beyond preserving audit integrity.

## Milestone 12. Production hardening

### Ціль

Підготувати BodyLife CRM v1 до production use: deployment, observability, security checks, performance, migration discipline, support runbooks, owner acceptance and final go-live gate.

### Залежності

- Milestones 1 through 11.
- Passed restore rehearsal and owner restore-check.
- Hosting/deployment target selected and accepted by owner/developer.

### Задачі

- Finalize staging and production deployment process for one web app and one PostgreSQL database.
- Run production migration procedure in staging using reviewed SQL/migration bundle.
- Confirm environment configuration, secrets handling, HTTPS, secure cookies/session settings and least-privilege DB/app access.
- Configure health checks, structured logs, error reporting and reliable downtime/backup-failure notification.
- Add metrics/alerts or operational checks for app availability, command errors, command/report latency, failed logins, permission denials, duplicate submissions, recalculation failures and backup status.
- Run full E2E regression on tablet and phone: search, profile, issue membership, mark/cancel visit, payment correction, freeze/non-working day, reports and audit/history.
- Run performance checks for quick reception actions, search, daily report and membership lists with realistic v1 data volume.
- Review support/correction workflow with owner/admin: wrong visit, wrong payment, wrong freeze, wrong non-working day, wrong card, fallback mismatch.
- Confirm sensitive data logging policy: no secrets/tokens, masked phone/personal data where appropriate, debug logs off by default.
- Confirm all out-of-scope surfaces are absent: client portal, public API, online payments, offline sync, multi-tenant, full import, complex accounting.
- Prepare production launch checklist and rollback/restore decision procedure.
- Record implementation ADR details: framework/runtime versions, DB provider, backup retention/PITR, migration policy, test gates, deploy procedure and restore evidence.

### Acceptance Criteria

- Staging environment matches production architecture closely enough for migration, restore and E2E confidence.
- Production deploy procedure is documented and rehearsed.
- Health checks and operational notifications can detect app unavailability and backup failure.
- Full regression suite passes: domain, application, PostgreSQL integration, migration, report consistency and Playwright tablet/phone.
- No known recalculation/report/audit consistency blockers remain.
- Owner/admin can complete core workflows and inspect audit/report explanations in UAT.
- Backup/restore readiness from Milestone 11 remains valid for the chosen production environment.
- Security review finds no default credentials, exposed secrets, missing HTTPS/session hardening or obvious role bypasses.
- Production go-live checklist is signed off by owner/developer.

### Потрібні тести

- Full automated regression suite.
- Playwright E2E on tablet and phone viewports for reception and owner workflows.
- PostgreSQL migration rehearsal and smoke after migration.
- Performance smoke for search, MarkVisit, IssueMembership, GenerateDailyReport and report lists.
- Security smoke: auth/role bypass, session cookie settings, CSRF/form protection, secret logging review.
- Observability smoke: command error creates log/error event with correlation id; failed backup/status path is visible by chosen mechanism.
- Restore/fallback spot-check after final production-like deployment.

### Ризики

- Last-minute hosting constraints can invalidate backup/restore assumptions.
- Performance issues can appear only with realistic report/search data.
- Production hardening can uncover missing product decisions, especially day close, one-off visits or multiple active memberships.
- Logs/metrics can leak personal data if reviewed too late.
- Scope pressure can add v2 surfaces before v1 is stable.

### Що не входить

- New business features beyond v1 scope.
- Client self-service portal, public API, online payments, POS, QR/NFC/turnstile, offline-first sync or multi-tenant SaaS.
- Full import pipeline.
- Advanced analytics/warehouse.
- Replacement of provider backup with custom in-app backup UI.

## Roadmap Done Criteria

- All 12 milestones are represented as issue-tracker-ready epics with goal, dependencies, tasks, acceptance criteria, tests, risks and explicit out-of-scope items.
- Dependencies are visible and no milestone assumes later business modules without naming the dependency.
- Every state-changing v1 workflow has a command owner, permission policy, transaction boundary, recalculation/audit expectation and tests before production.
- Reports, audit and operations are implemented as trust-building capabilities, not afterthoughts.
- Production use waits for restore rehearsal, owner restore-check and production hardening gates.
