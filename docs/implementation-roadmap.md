# BodyLife CRM v1 implementation roadmap

Дата: 2026-07-07, оновлено 2026-09-06
Статус: чинний план; Milestones 1-10.5 та ADR-020 виконані, ADR-021 corrective
slice: 21.1 contracts узгоджені, наступний крок 21.2 persistence;
погоджений Milestone 10.6 іде після нього, Milestone 11 -
після 10.6

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
| 10.5. ADR-018 sales, negative coverage and replacement | 5 through 10 | Closed core workflows required before operations readiness |
| ADR-021 corrective slice | 5 through 10.5 plus ADR-020 | One lifecycle-active Membership without hidden historical debt |
| 10.6. Single-visit sales and reception defaults | 3, 4, 6, 7, 9, 10, 10.5, ADR-021 | Coherent paid one-off/trial reception workflows |
| 11. Backup/restore/paper fallback readiness | 1 through 10.6 | Production readiness evidence |
| 12. Production hardening | 1 through 11, including 10.5 and 10.6 | Go-live |

## Cross-Cutting Rules

- Every state-changing workflow is a server-side command/action with authorization, validation, idempotency where needed, transaction boundary, recalculation decision, audit entry and canonical reread.
- Memberships is the only owner of membership formulas: remaining visits, negative balance, first negative visit date, extension days, effective end date and warnings.
- Reports and UI read canonical state; they do not calculate membership truth locally.
- UI follows `docs/ui-design-foundation.md` for shared layout/components, warning visibility, tablet-first and phone-friendly consistency.
- Corrections and cancellations preserve source history and append audit. They do not hard-delete or silently patch business records.
- Backdated/manual/paper fallback entries use normal domain commands with `occurred_at`, server `recorded_at`, `entry_origin`, actor/session and reason/comment. Paper fallback additionally requires one numbered-sheet batch and one unique first-class row reference per entry.
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
- Accepted product decisions: inclusive date arithmetic in ADR-005; explicit
  Visit selection/no-active behavior and Freeze blocking in ADR-014; ADR-018
  and ADR-020 define negative closure/automatic issue coverage. ADR-021
  supersedes ADR-014 multiple-active cardinality in the corrective slice below.

### Задачі

- Створити `issued_memberships`, `membership_opening_states`, `membership_adjustments`, `membership_state_cache`, `membership_extension_days`.
- Реалізувати `IssueMembership` without silent negative hiding. Milestone 10.5
  replaces the earlier optional sale behavior with the accepted ADR-018
  exact-price Membership-and-Payment transaction.
- Copy immutable MembershipType snapshot on issue: type name, duration, visits limit, price.
- Implement base end date formula: `start_date + duration_days - 1 day`, unless business confirms different convention before tests lock.
- Implement Memberships recalculation service for source facts available at this stage: issued membership, opening state, adjustments, future visit/payment/freeze/non-working inputs through public interfaces.
- Implement signed remaining visits, negative balance, first negative visit date, effective end date, extension days and warnings.
- Implement `GetMembershipState` and membership section of `GetClientProfile`.
- Implement `PreviewIssueMembership`, including the negative-balance warning, ADR-020 automatic oldest-first concrete-Visit allocation and signed stale-preview protection.
- Add rebuild command/service for `membership_state_cache` from source facts.
- Add guardrails so UI, Reports, Visits, Payments, Freezes and NonWorkingDays cannot own formulas.

### Acceptance Criteria

- Issued membership stores immutable snapshot values and keeps them after MembershipType edit.
- Base end date and active-by-date behavior follow the accepted inclusive date convention.
- `membership_state_cache` is derived and rebuildable from source facts.
- Memberships public query is the only source for remaining visits, negative balance, first negative date, effective end date, extension days and warnings.
- Direct effective-end-date edit is impossible through ordinary workflows.
- Issuing with existing negative state applies ADR-020 automatic concrete
  coverage and keeps concrete/unknown remainder visible; ADR-021 then owns the
  explainable predecessor lifecycle transition.
- Client profile reads Memberships state through public query after command success.
- Recalculation failure causes command failure/rollback rather than partial success.

### Потрібні тести

- Domain tests for inclusive end date, active-by-date, snapshot immutability and direct end-date edit rejection.
- Domain tests for remaining visits from counted visits, negative balance, first negative date, cancellation recalculation hooks using synthetic/source fixtures.
- Application tests for `IssueMembership`, `PreviewIssueMembership`, opening state/manual backfill metadata, automatic coverage, zero-capacity eligibility and stale preview protection.
- Rebuild tests comparing `membership_state_cache` with recalculated state.
- PostgreSQL tests for issued membership constraints, active opening state uniqueness and cache row uniqueness.
- Architecture tests/reviews to prevent formula duplication outside Memberships.

### Ризики

- ADR-014 selection/ordering rules can drift if Visits or UI infer a Membership instead of using the Memberships-owned public boundary.
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
- ADR-014 accepted for explicit Visit allocation, no-active contexts, ordering
  and Visit-during-Freeze blocking. ADR-021 supersedes only its multiple-active
  cardinality decision.

### Задачі

- Створити `visits`, `visit_consumptions`, `visit_cancellations`.
- Реалізувати `MarkVisit` with selected membership or explicit one-off/trial context.
- Apply ADR-014: always validate the explicit `membership_id`; acknowledge an
  expired current Membership; reject future-start, source-consistency ambiguity
  and Visit during active Freeze; create no consumption for one-off/trial.
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
- `MarkVisit` never chooses a Membership implicitly; after ADR-021, multiple
  lifecycle-active candidates are a source-consistency failure. one-off/trial
  creates no membership consumption or Memberships state change.
- Visit before Membership start or during an active inclusive Freeze is rejected; expired Membership proceeds only through explicit selection and acknowledgement.
- Recording a visit at 0 remaining visits is allowed only with explicit warning acknowledgement and produces negative state.
- First negative visit date is recalculated by Memberships.
- Canceling a visit preserves visible history, deactivates counted consumption, recalculates membership state and excludes the visit from active visit totals.
- Duplicate tap/submit does not create duplicate visits.
- Visit command commits source fact, consumption, recalculation and audit together or rolls back together.
- Profile rereads canonical state after visit/cancellation success.
- Canceled visits do not count as last counted visit for inactive-client reporting.

### Потрібні тести

- Domain tests for remaining visits, zero-to-negative, multiple negative visits and first negative date.
- Domain/application tests for ADR-014 explicit selection, ADR-021
  source-consistency ambiguity rejection, expired/future-start eligibility,
  explicit one-off/trial contexts, same-date ordering and Freeze blocking.
- Domain/application tests for canceling normal visit and canceling first negative visit.
- Application command tests for permissions, warning acknowledgement, idempotency, concurrency conflict and rollback on recalculation/audit failure.
- PostgreSQL tests for at most one active counted consumption per visit and FK/client-membership consistency.
- Report-source tests for active vs canceled visit rows.
- Playwright tests: mark visit, acknowledge negative warning, cancel visit, verify profile refresh.

### Ризики

- UI or command shortcuts can still create wrong consumptions if they bypass ADR-014 explicit selection.
- One-off/trial rows can accidentally affect Memberships if visit kind and consumption constraints are not strict.
- Quick reception UI can double-submit without strong idempotency.
- Freeze checks can drift between current and backdated Visits unless every command applies ADR-014 to the Visit business date.

### Що не входить

- Payment creation/correction.
- Full daily report UI, beyond source data readiness.
- One-off/trial product polish inside Milestone 6; it was later explicitly
  selected for v1 as Milestone 10.6 under ADR-019.
- Turnstile/check-in automation, QR/NFC or self check-in.
- Full day close/reconciliation workflow unless a separate decision adds it.

## Milestone 7. Payments and corrections

### Ціль

Реалізувати основу окремих готівкових оплат: створення, виправлення або
скасування зі збереженням історії та правдивим денним звітом. Продаж
абонемента і погашення мінуса не є окремими оплатами; повні правила ADR-018
реалізуються в Milestone 10.5.

### Залежності

- Milestone 2.
- Milestone 3.
- Milestone 5 for client history and future sale integration.
- Milestone 6 for combined reception/profile/report consistency.

### Задачі

- Створити `payments`, `payment_cancellations`, `payment_corrections`.
- Реалізувати `CreatePayment` only for accepted standalone cash contexts.
- Реалізувати `CorrectPayment` only for standalone Payment, with replace/cancel
  mode, reason/comment and old/new occurred date explainability.
- Enforce `amount > 0`, `method = cash`, valid client/context and entry-origin
  metadata.
- Reject `membership_sale` and `negative_closure` in both generic commands;
  these complete actions are owned by Milestone 10.5.
- Update daily cash report source queries to read canonical payment status/replacement rows.
- Add idempotency for quick payment creation and correction/cancellation.
- Додати окрему payment UI action and standalone correction flow from
  profile/history/report drill-down.
- Add audit events: `payment.created`, `payment.corrected`, `payment.canceled`.

### Acceptance Criteria

- Cash payment appears in client history and daily cash source rows after commit.
- Standalone payment never changes Membership state, completes an ordinary sale
  or closes negative Visits.
- Generic payment commands reject sale-linked and closure-linked contexts.
- Correct/cancel standalone Payment preserves original fact and creates
  replacement/correction/cancellation facts.
- Corrected amount/date changes live daily cash totals through canonical records, not manual report patches.
- Corrections after closed/reconciled day are Owner-only or follow explicit owner-approved policy.
- Duplicate payment submit does not create duplicate cash rows.
- Audit includes before/after payment summary, reason/comment and changed-after-close marker where applicable.

### Потрібні тести

- Command tests for `CreatePayment` validation, permission, idempotency and audit.
- Command tests for `CorrectPayment` replace/cancel modes, reason requirement,
  sale/closure rejection, day-close permission and old/new date explainability.
- Report consistency tests for daily cash count/sum before and after correction/cancellation.
- PostgreSQL tests for amount/check constraints and FK membership-client consistency.
- UI tests for add payment, duplicate-submit protection and payment correction.

### Ризики

- Payment existence may be incorrectly treated as closing negative visits.
- Payment replacement model can become confusing if original/replacement are not clear in history.
- A generic Payment command can accidentally bypass the complete sale or
  negative-coverage action unless its context is constrained.
- Cash day close policy is referenced by operations but not fully defined as a command.

### Що не входить

- Online payments, bank terminals, POS or accounting integration.
- Complex receivables/debt ledger.
- Ordinary Membership sale, negative coverage and their correction commands;
  Milestone 10.5 implements them as complete ADR-018 actions.
- Partial-payment accounting.
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
- ADR-014 and ADR-015 for Visit/Freeze conflict symmetry, Freeze range
  eligibility and Membership-first locking.
- ADR-016 for NonWorkingDay lifecycle/date eligibility, full-period
  contribution, immutable confirmed scope and correction semantics.

### Задачі

- Створити `freezes`, `freeze_cancellations`, `non_working_periods`, `non_working_period_applications`, `non_working_period_cancellations`.
- Apply ADR-015 in AddFreeze: lifecycle-active Membership, eligible start bound
  against locked pre-command state, unclipped end, counted-Visit conflict and
  shared Membership-first lock order.
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
- Freeze starts no earlier than Membership start and no later than locked
  pre-command effective end; an eligible range may end after that effective end
  and is not clipped.
- AddFreeze rejects active counted Membership Visit overlap with
  `freeze_conflicts_with_visit`; canceled and one_off/trial Visits do not block.
- Canceling freeze preserves history and removes its active extension days.
- NonWorkingDay add/correction is Owner-only and requires preview/affected-scope confirmation.
- NonWorkingDay scope contains lifecycle-active Memberships with any inclusive
  overlap against canonical pre-command state calculated without the proposed
  or replaced period.
- Every confirmed NonWorkingDay application contributes the full inclusive
  period without clipping to Membership boundaries.
- Freeze and NonWorkingDay overlap counts union calendar days, not sum of sources.
- NonWorkingDay application scope is stored as an immutable Owner-confirmed
  transaction snapshot and is explainable; later Membership/source changes do
  not silently add or remove applications.
- Correcting/canceling NonWorkingDay recalculates old and new affected memberships.
- Profile shows extension reasons and history for freeze/non-working sources.
- Recalculation and audit are committed consistently; failure blocks success.

### Потрібні тести

- Domain tests for inclusive freeze days, lifecycle/start eligibility, end beyond
  pre-command effective end, counted-Visit conflict, canceled freeze, plus
  NonWorkingDay lifecycle/overlap eligibility, full-period endpoint behavior,
  proposed-source exclusion and overlap union.
- Application tests for Add/CancelFreeze permissions, validation, Membership-first
  locking, concurrency with MarkVisit, idempotency, audit and rollback.
- Application tests for Preview/Add/CorrectNonWorkingDay: owner-only, exact
  scope/range fingerprint, preview expiry, affected_scope_changed, immutable
  snapshot, old/new correction scope, overlap warning and recalculation.
- PostgreSQL tests for date range constraints and application rows.
- Performance/transaction tests for realistic affected membership counts.
- UI tests for add freeze, cancel freeze, non-working preview/confirm/correct.

### Ризики

- NonWorkingDay mass recalculation can be slow or partially applied.
- Preview can become stale between view and commit.
- Overlap rules can be accidentally double-counted.
- MarkVisit and Add/CancelFreeze lock-order drift can reintroduce stale
  Visit/Freeze eligibility windows.
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


## Стоп після ADR-020; ADR-021 corrective slice є наступним

Milestone 10.5 and ADR-020 are implemented and validated. ADR-021 is accepted
and planned but not implemented; its one-active-Membership corrective slice is
the next roadmap work and must finish before Milestone 10.6. ADR-019/Milestone
10.6 remain accepted and planned, and Milestone 11 follows them without having
started.

## Milestone 10.5. ADR-018 membership sales, negative coverage and replacement

### Ціль

Implement accepted ADR-018 without treating it as already delivered: exact ordinary-sale payment invariant, catalog kinds, oldest-first negative closure/coverage, issued-sale replacement/cancel and first-class paper-sheet metadata.

### Залежності

- Milestones 5 through 10: Memberships state, Visits, Payments, reports and audit/history.

### Задачі

- Add PostgreSQL schema/source facts and constraints for type kind, exact sale links, one-off line snapshots, coverage allocations, replacement/cancel facts and paper batch sheet/line metadata.
- Consolidate the pre-deployment EF migration chain into one reviewable
  greenfield baseline that creates the current schema and PostgreSQL
  invariants. No historical classification or upgrade path is required while
  the application has no deployed database or production data.
- Implement `IssueMembership` exact sale, one-off closure/correction,
  paymentless opening-state creation, issued-sale replace/cancel and paper
  batch-row commands with deterministic locks, authorization, idempotency,
  recalculation, audit/history and canonical report rereads.
- Block generic `CreatePayment`/`CorrectPayment` from sale and negative-closure
  contexts.
- Build Razor/htmx preview/confirmation flows that show all methods/consequences without recommending a method/type.

### Acceptance Criteria

- Omitted, under and over ordinary sale payment reject with no partial Membership/Payment/audit state.
- Zero or two active sale Payments, a price/currency mismatch and generic
  payment-command bypass are rejected.
- Manual opening-state declaration commits with zero sale Payments and cannot
  be mistaken for an ordinary sale.
- A clean database applies the sole baseline, reaches the current EF model and
  preserves all sale, coverage and paper-row invariants without fabricating
  historical Membership or Payment facts.
- Catalog edits preserve issued and closure snapshots; partial/full one-off closure and oldest-first coverage remain explainable.
- Repeated or concurrent allocation cannot cover one Visit twice.
- New-Membership coverage accepts counts from 1 through its visit limit,
  rejects 0 and limit + 1, and leaves any excess old negative Visits visible.
- New-Membership coverage is backdated to the oldest covered Visit and may visibly be expired.
- Replacement/cancel rolls back on dependency, concurrency, recalculation or audit failure; reports/audit preserve cancellation/replacement explanation.
- Admin/Owner is allowed and an unauthorized actor is rejected for every
  ADR-018 correction command.
- Paper fallback row has first-class sheet/line metadata, duplicate line is
  rejected and reconciliation uses canonical report/history/audit.

### Потрібні тести

- PostgreSQL tests for zero/two sale Payments, snapshot-price/currency mismatch,
  paymentless opening mode, context checks, one active coverage per Visit,
  allocation limit boundary, line totals, duplicate sheet line, FK consistency
  and deterministic row locks.
- Migration tests for clean baseline apply, no-op reapply, rollback/reapply,
  current-model drift and the PostgreSQL sale/coverage/paper-row invariants.
- Command tests for omitted/under/over payment attempts, generic
  CreatePayment/CorrectPayment bypass, partial/full oldest-first closure,
  new-Membership coverage at 0/1/limit/limit+1, concurrent repeated allocation,
  Admin/Owner/unauthorized permissions, dependency rollback and idempotent
  retry after both success and failure.
- Report/audit consistency tests; tablet/phone UI flows; paper-sheet
  reconciliation drill.
### Що не входить

- Day close/reconciliation policy, refunds/deltas/cash transfer accounting or direct mutation of dependent facts.

## Corrective slice ADR-021. One lifecycle-active Membership per Client

### Ціль

Replace the ADR-014 multiple-active model with one database-enforced
lifecycle-active Membership per Client without hiding concrete or unknown
negative history. Keep ADR-020 new-Membership coverage and ADR-018 one-off
closure as separate, explainable ways to resolve concrete Visit debt.

### Залежності

- Milestone 10.5 and ADR-020 for exact Membership sales, oldest-first coverage,
  signed issue preview, negative-closure correction and canonical debt state.
- Milestones 5, 6, 8, 9 and 10 for Memberships recalculation, Visit/Freeze
  eligibility, reports, history and business audit.
- Accepted ADR-021 and the greenfield sole-baseline policy. There is no deployed
  production database or legacy-row migration requirement.

### Малі завершувані кроки

1. **21.1 Contract alignment.** Update architecture baseline, domain model, data
   architecture, interaction contracts, UI workflows, operations/audit and
   quality expectations. Remove contradictory multiple-active and
   active-status-only negative-query wording before code changes.
   Completed as documentation-only Step 250 on 2026-09-06; runtime lifecycle
   behavior remains pending. Next: 21.2 only.
2. **21.2 Lifecycle persistence.** Add `closed`, append-only Membership closure
   source facts, reason/successor links, lifecycle mappings and a PostgreSQL
   partial unique index for one `active` row per Client. Fold everything into
   the sole `InitialBaseline` and prove clean apply/current-model invariants.
3. **21.3 Atomic transitions.** Extend Issue preview/token and command locks for
   zero, positive, concrete/unknown/mixed negative, expired, future-start,
   backdated and paper-fallback states. Close eligible predecessor, create the
   successor sale/allocation and audit in one transaction; block silent positive
   forfeiture and stale/concurrent transitions.
4. **21.4 Coverage, reports and UI.** Let ADR-020 and one-off closure operate on
   open historical concrete Visit debt from `closed` Memberships without
   allowing new Visits or Freezes on them. Keep unknown opening remainder
   visible but non-coverable pending a separate reconciliation decision. Update
   current/history/negative queries, profile, warnings, reports and audit
   explanations for one current Membership plus explainable historical debt.
5. **21.5 Acceptance.** Add correction/cancellation dependency regressions,
   PostgreSQL concurrency and constraint tests, report/audit consistency and
   tablet/phone flows. Run focused gates, full `scripts/validate.sh`, independent
   acceptance review, progress update and logical commit before 10.6.1.

Execution is sequential. After every step: run the focused gate, update
`docs/implementation-progress.md`, stage only owned files, make one logical
commit and stop with the next recommended step. Do not batch this corrective
slice with Milestone 10.6.

### Acceptance Criteria

- PostgreSQL cannot commit two lifecycle-active Memberships for one Client,
  including concurrent, backdated and paper-fallback issue attempts.
- Zero-balance predecessor closes atomically with the successor Membership and
  keeps its original exact-price Payment active and explainable.
- Concrete/unknown/mixed negative predecessor can be superseded after the
  ADR-020 concrete allocation step; concrete residual remains visible and
  coverable after it is `closed`, while unknown remainder remains visible and
  is never synthesized or Visit-covered.
- Partial one-off closure keeps the sole negative Membership active; full
  closure closes it at zero. One-off coverage of historical concrete Visit debt
  does not alter an unrelated current Membership.
- Positive remaining visits, including expired-by-date or future-start cases,
  reject ordinary issue without writes or silent forfeiture.
- Current Membership queries return only `none` or `single`; Visit, Freeze and
  NonWorkingDay choices never target `closed`, while Negative Clients/history
  retain all open debt and provenance.
- Every closure has one unique source Membership, a distinct optional
  same-client successor and commit-time status/fact consistency.
- Visit/coverage correction that would make a closed non-positive Membership
  positive is dependency-blocked with no writes; no automatic reactivation,
  visit transfer or silent unusable credit occurs.
- Issue/correction retries, stale preview, unique conflicts, recalculation or
  audit failures leave no partial closure, Membership, Payment or allocation.
- A clean PostgreSQL database applies the sole baseline and reaches the current
  EF model with lifecycle closure and one-active invariants.

### Потрібні тести

- Domain/application matrix tests for zero/positive/negative/unknown/mixed,
  expired/future/backdated states, closure reasons, canonical rereads and no
  automatic predecessor reactivation.
- PostgreSQL tests for the partial unique index, deterministic lock order,
  concurrent Issue/full-one-off/correction transitions, closure
  uniqueness/FKs/checks, transaction rollback and clean baseline
  apply/no-op/rollback-reapply/current-model drift.
- ADR-018/020 command tests proving oldest-first allocation, partial/full
  remainder, exact Payments, correction dependencies, idempotency and
  `stale_state` behavior across active and closed source Memberships, including
  non-positive-to-positive correction rejection.
- Report/history/audit consistency and tablet/phone Playwright flows proving one
  current Membership, visible historical debt, blocked positive rollover and
  understandable predecessor/successor explanation.

### Що не входить

- Queued/future Memberships, transferable leftover visits, automatic rollover
  schedules or a second concurrently active Membership.
- Silent forfeiture of positive visits or a new manual early-close/forfeit
  command; that requires a separate explicit product decision.
- Runtime repair/import of an already-deployed multiple-active database.

## Milestone 10.6. Single-visit sales and reception defaults

### Ціль

Implement ADR-019 as a bounded product slice: Owner-managed one-off tariffs and
Reception defaults, paid named-client one-off sales, global anonymous trial
sales through a protected technical Client, and one linked cancellation that
keeps Visit, Payment, reports and audit explainable. Milestone 10.5 negative
closure remains intact; Milestone 11 does not start until 10.6 acceptance.

### Залежності

- Milestones 3, 4, 6, 7, 9 and 10 for Clients, MembershipTypes, Visits,
  Payments, reports and audit/history.
- Milestone 10.5 for active `one_off` catalog rules, exact Payment patterns,
  PostgreSQL transaction/locking conventions and negative-closure regression.
- Completed ADR-021 corrective slice, so single-visit work cannot preserve or
  reintroduce multiple lifecycle-active Membership assumptions.
- Accepted ADR-019 and the greenfield single-baseline policy. No deployed
  database, production data migration or historical link invention is needed.

### Малі завершувані кроки

1. **10.6.1 Contract alignment.** Update `architecture-baseline`, domain model,
   data architecture, interaction contracts, UI workflows, UI design
   foundation, operations/audit matrix and quality expectations for ADR-019.
   Name module ownership and remove contradictory raw one-off/trial product
   wording before code changes.
2. **10.6.2 Owner catalog and defaults.** Expose immutable
   `ordinary`/`one_off` kind at MembershipType creation, enforce the one-visit
   positive-price shape, display kind clearly, and add Owner-only optimistic
   settings for default one-off and trial types. Allow one type in both roles,
   audit changes, block deactivation while assigned and never choose a fallback
   silently.
3. **10.6.3 Protected trial Client.** Add exactly one `system_trial` Client with
   deterministic idempotent bootstrap and database uniqueness. Exclude it from
   ordinary search, duplicate checks, profile/client mutations, Membership
   issue, normal Visit/Payment commands and inactive-client reports while
   preserving translated report/audit explanation.
4. **10.6.4 Aggregate persistence and commands.** Add a Visits-owned
   `SingleVisitSale` source aggregate linking exactly one Visit and one exact
   cash Payment with the same Client/purpose/time plus immutable tariff
   snapshot. Fold schema, FKs, unique/deferred commit invariants and indexes into
   `InitialBaseline`; implement `CreateOneOffSaleForClient`, `RecordTrialSale`
   and supporting queries for Owner, named Admin and shared Reception/Admin with
   idempotency, deterministic locks, stale checks, rollback,
   entry-origin/paper-row metadata, one aggregate audit event and canonical
   rereads. Reject generic creation/correction bypasses.
5. **10.6.5 Named-client one-off UI.** Replace the raw one-off option in Mark
   Visit and Add Payment with a separate paid action on the Client profile. Show
   all active one-off tariffs, preselect only the configured default, display
   server price read-only, keep Client/Membership warnings visible, prevent
   duplicate submit and reread canonical Visits/Payments after success.
6. **10.6.6 Global trial UI.** Remove trial from the named-client path and add a
   Reception dashboard action that needs no search/profile. Show the configured
   trial tariff and exact price; disable with a clear configuration message when
   absent; commit against the system trial Client and return a receipt-like
   operation result without exposing a fake Client profile.
7. **10.6.7 Linked cancellation and explanation.** Add reason-required
   `CancelSingleVisitSale` for Owner, named Admin and shared Reception/Admin on
   current or older dates. Cancel the linked Visit and Payment atomically,
   reject generic child cancellation or correction, preserve retry/concurrency
   behavior, update original-day live totals together and cross-link
   sale/cancellation in history, reports and audit. Preserve
   `changed_after_close` only when an existing/future reconciliation marker
   applies; add no refund/delta row or day-close ledger.
8. **10.6.8 Negative-closure default and acceptance.** Reuse the configured
   default only to initialize the first type after Actor deliberately selects
   one-off closure; never preselect method or quantity and do not change
   oldest-first allocation/correction rules. Run focused regressions, complete
   PostgreSQL/report/audit/tablet/phone gates, full `scripts/validate.sh`, an
   independent acceptance review and final progress update.

Execution is sequential. After every step: run the focused gate, update
`docs/implementation-progress.md`, stage only owned files, make one logical
commit and stop with the next recommended step. Do not combine the eight slices
into one implementation pass.

### Acceptance Criteria

- Owner can create/edit/deactivate ordinary and one-off catalog types, while
  non-Owner actors cannot; kind and historical snapshots remain immutable.
- Owner can save/clear both defaults, assign one type to both roles and sees an
  exact stale/configuration-in-use error instead of silent fallback.
- Named-client one-off sale creates one aggregate, one `one_off` Visit, one
  exact-price cash Payment and one aggregate audit entry in one transaction.
- Trial sale creates the same shape with purpose `trial`, configured tariff and
  the singleton technical Client, without asking Reception for a person/profile.
- Owner, named Admin and shared Reception/Admin can create/cancel these sales;
  every unauthorized account is rejected before writes.
- Neither workflow creates an Issued Membership, Visit consumption, hidden
  negative coverage or Memberships state change.
- Catalog edits affect future sales only; old sales/report/audit rows retain
  type/name/price/currency snapshots.
- Missing/stale/inactive configuration, wrong Client kind, wrong price/currency,
  duplicate submit, concurrency conflict or child/audit failure leaves no
  partial sale facts.
- Generic MarkVisit/CreatePayment reject `one_off`/`trial` contexts outright;
  generic CancelVisit/CorrectPayment cannot split a linked sale.
- Linked cancellation is reason-required, idempotent, available to Admin/Owner
  for older dates and cancels both child facts or neither.
- Daily visit and cash totals exclude canceled child facts together while
  drill-down/history/audit preserve the original sale and later cancellation.
- Technical trial Client never appears as an ordinary searchable/inactive
  person but every trial remains reachable from report/audit explanation.
- Existing one-off negative closure remains oldest-first and regression green;
  default initialization never chooses its method or quantity.
- Normal and `paper_fallback` entry metadata remain complete and explainable.
- A clean PostgreSQL database applies the sole baseline and reaches the current
  EF model with all aggregate/configuration/technical-client invariants.

### Потрібні тести

- Domain/application tests for tariff/default validation, same-type defaults,
  missing configuration, the full Owner/named Admin/shared/unauthorized role
  matrix, stale type/settings, snapshots, idempotency, later-date cancellation
  and canonical reread targets.
- PostgreSQL tests for singleton settings/technical Client, FK/client-purpose
  consistency, unique Visit/Payment links, exact amount/currency, deferred
  aggregate constraints, transaction rollback, deterministic lock ordering,
  concurrent create/cancel and raw generic create attempts that reject without
  committing any Visit, Payment or audit rows.
- Migration tests for clean baseline apply, no-op reapply, rollback/reapply,
  current-model drift and all new PostgreSQL invariants.
- Report/history/audit consistency tests proving linked active/canceled totals,
  technical-client presentation, immutable tariff explanation, aggregate event
  matrix coverage and preserved negative-closure behavior.
- Playwright tablet/phone flows for Owner kind/default configuration,
  named-client one-off, global trial, missing/stale configuration, busy
  double-tap prevention, linked cancellation reason/errors and canonical
  Visits/Payments/report/audit rereads.

### Ризики

- A technical Client can pollute search, duplicate detection or inactive-client
  reports if its protected kind is treated as an ordinary person.
- Matching separate facts by timestamp/client instead of a constrained
  aggregate can leave unexplained or half-canceled Visit/Payment rows.
- Generic command paths can bypass exact-price/snapshot rules unless rejected
  server-side and tested.
- Catalog/default edits between preview and submit can charge a stale price
  without locking and optimistic version checks.
- Two raw audit events can look like two manual actions; aggregate audit and
  related ids must explain one Reception command.
- Applying the default before explicit negative-closure method choice would
  silently weaken ADR-018 deliberate-choice behavior.

### Що не входить

- Free trials or zero-price sales.
- Anonymous one-off sales; the technical Client is trial-only in v1.
- Trial visitor identity/lead capture, conversion or merge into a future Client.
- Coupons, scheduled promotions, automatic discount rules or complex product
  taxonomy; Owner uses catalog edit/create/remap for future prices.
- Quantity bundles, reservations, turnstile/check-in automation or self-service.
- Receipts, POS/cash drawer, online payments, refunds, cash deltas, accounting
  settlement or day-close ledger.
- Generic amount/type correction of a linked sale. Cancel it with reason and
  create the correct sale.
- Replacing or merging ADR-018 negative-closure facts/commands.

## Milestone 11. Backup/restore/paper fallback readiness

### Ціль

Довести, що production data can be recovered and outage work can be reconciled without direct DB edits: provider backups, restore runbook, restore rehearsal, owner restore-check, paper fallback template and backdated/fallback entry workflow.

### Залежності

- Milestone 1 for deployment/migration foundation.
- Milestones 2 through 10.6 for business data, audit, reports, ADR-018
  corrections, linked one-off/trial sales and paper-batch workflow.
- Chosen hosting/provider backup capabilities.

### Задачі

- Configure provider-managed automated backups for full backup scope: database, migration version, app configuration needed for restore and uploaded files if introduced.
- Confirm minimum 30-day retention and RPO not worse than 24 hours; prefer PITR/several-hour RPO if provider supports it.
- Write restore runbook matching actual deployment and migration process.
- Execute restore rehearsal into isolated staging/test environment.
- Run restore-check procedure with owner: login, search known client, profile state, daily report, audit/history, fallback/backfill labels if present.
- Record evidence: snapshot timestamp, rehearsal time, operator, restored environment, schema version, observed RPO/RTO, owner result and follow-ups.
- Add rebuild/consistency checks after restore: current card uniqueness, membership state cache rebuild comparison, daily cash sample, recent audit rows.
- Create paper fallback template with numbered sheet, stable line number,
  client/card, event type, `occurred_at`, payment/range/source and explanation.
- Finalize `entry_batches`/`entry_batch_rows` for `manual_backfill` and
  `paper_fallback`.
- Ensure fallback/backdated visits/payments/freezes/memberships use normal
  commands with `entry_origin`, first-class batch row, reason/comment,
  validation, recalculation and audit.
- Document reconciliation process: enter paper rows, generate daily reports, compare cash/visit totals, inspect drill-down/audit, correct mismatches only via commands.

### Acceptance Criteria

- Automated backups are enabled and documented for the production-like environment.
- Restore runbook matches the actual hosting/database/migration setup.
- At least one pre-production restore rehearsal passes in isolated environment.
- Owner completes restore-check and blocking discrepancies are fixed or rehearsal repeated.
- Rebuilt membership state matches stored `membership_state_cache` on restored copy or drift is explained and fixed.
- Paper fallback template is ready and understandable by reception/admin staff.
- Paper fallback entries can be entered with `entry_origin = paper_fallback`, actual `occurred_at`, server `recorded_at`, actor/session, paper sheet batch and stable line reference.
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

- Milestones 1 through 11, including Milestones 10.5 and 10.6.
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
- Production hardening can uncover missing product decisions, especially day
  close or other explicitly open operational policies. ADR-016 and ADR-018
  already settle NonWorkingDay range boundaries and one-off negative closure.
- Logs/metrics can leak personal data if reviewed too late.
- Scope pressure can add v2 surfaces before v1 is stable.

### Що не входить

- New business features beyond v1 scope.
- Client self-service portal, public API, online payments, POS, QR/NFC/turnstile, offline-first sync or multi-tenant SaaS.
- Full import pipeline.
- Advanced analytics/warehouse.
- Replacement of provider backup with custom in-app backup UI.

## Roadmap Done Criteria

- All 12 numbered milestones, Milestones 10.5/10.6 and the ADR-021 corrective
  slice are represented as issue-tracker-ready epics with goal, dependencies,
  tasks, acceptance criteria, tests, risks and explicit out-of-scope items.
- Dependencies are visible and no milestone assumes later business modules without naming the dependency.
- Every state-changing v1 workflow has a command owner, permission policy, transaction boundary, recalculation/audit expectation and tests before production.
- Reports, audit and operations are implemented as trust-building capabilities, not afterthoughts.
- Production use waits for restore rehearsal, owner restore-check and production hardening gates.
