# BodyLife CRM v1 vertical slice plan

Дата: 2026-07-07
Статус: implementation planning draft

Основа: `docs/architecture-baseline.md`, `docs/domain-model.md`, `docs/data-architecture.md`, `docs/interaction-contracts.md`, `docs/ui-workflows.md`, `docs/ui-design-foundation.md`, `docs/operations-design.md` і `docs/technology-stack-decision.md`.

Цей документ описує перший vertical slice для перевірки архітектури BodyLife CRM v1. Slice не є повним MVP. Його ціль - довести, що selected modular monolith stack, server-side commands, Memberships recalculation, append-only audit і report consistency можуть працювати разом у мінімальному reception workflow.

## 1. Slice goal

Побудувати вузький reception-oriented slice, який проходить один реальний бізнесовий ланцюжок:

1. Reception/Admin знаходить існуючого клієнта.
2. Видає йому абонемент із cash payment.
3. Додає коротку ADR-015-eligible Freeze range без active counted Membership Visit overlap як source reason для продовження.
4. Відмічає visits до нуля і один negative visit з явним warning acknowledgement.
5. Скасовує помилковий negative visit з reason/comment.
6. Коригує cash payment з reason/comment.
7. Перевіряє, що client profile, membership state, daily report drill-down і audit timeline показують одну й ту саму правду.

Slice перевіряє такі архітектурні твердження:

- state-changing дії йдуть через server-side commands/actions;
- source fact, recalculation і audit commit відбуваються разом або разом rollback;
- Memberships є єдиним owner membership formulas;
- UI і Reports перечитують canonical state, а не рахують бізнес-стан локально;
- daily report totals збігаються з drill-down source rows;
- corrections/cancellations не видаляють історію і видимі через audit;
- ASP.NET Core + Razor Pages/MVC + htmx + EF Core/Npgsql + PostgreSQL достатньо для core reception UX і transactional consistency.

Success of this slice дає підставу приймати рішення про повну реалізацію v1. Failure має показати, що саме треба змінити: module boundaries, transaction model, data schema, UI interaction model, ORM/query approach або stack choice.

## 2. User scenario

User story:

As a Reception/Admin user, I want to open a client by card, issue a membership, take cash, record visits including a negative visit, correct mistakes, and see the profile, report and audit agree, so that the owner can trust BodyLife CRM instead of paper/Excel for daily reception work.

Concrete scenario for the slice:

1. Test data contains one `shared_reception_admin` account/session, one Owner account for correction checks if needed, one active `MembershipType` named `Slice 2 visits / 30 days`, and one Client with current card number `BL-1001`.
2. Reception opens the dashboard, searches `BL-1001`, and gets an exact unique card match.
3. Reception opens the client profile and issues `Slice 2 visits / 30 days` starting `2026-07-01`, with cash payment `1000 UAH`.
4. System copies membership type snapshot, calculates `base_end_date = 2026-07-30`, stores initial `membership_state_cache`, stores payment, writes audit, and rereads the profile.
5. Reception adds Freeze `2026-07-10..2026-07-11` with reason `medical pause`. Its start is inside the locked pre-command Membership window and no active counted Membership Visit overlaps the range. System recalculates `extension_days = 2` and `effective_end_date = 2026-08-01`.
6. Reception records visits on `2026-07-01` and `2026-07-02`. Remaining visits becomes `0`.
7. Reception records a visit on `2026-07-03` after acknowledging the zero/negative warning. Remaining visits becomes `-1`, `negative_balance = 1`, `first_negative_visit_date = 2026-07-03`.
8. Reception discovers the `2026-07-03` visit was mistaken and uses `CancelVisit` with reason. The visit remains visible as canceled, remaining visits returns to `0`, negative state clears, and the daily report excludes that visit from totals while showing the cancellation in drill-down/history.
9. Reception or Owner corrects the cash payment from `1000 UAH` to `900 UAH` with reason. The original payment remains explainable; daily cash total for `2026-07-01` becomes `900 UAH`.
10. Owner/Admin opens daily report and audit timeline. The report totals, drill-down rows, client profile state and audit entries all reconcile to the same source records.

## 3. Scope

Involved modules:

- `Users/Roles`: actor context, shared Reception/Admin honesty, role checks for commands.
- `Clients/Search`: exact card search, profile identity, current card display.
- `MembershipTypes`: active catalog read for issue flow; create/edit/deactivate UI is not part of the slice.
- `Memberships`: issued membership snapshot, recalculation, state cache, warnings, extension days, negative state.
- `Visits`: visit source facts, counted consumption, cancellation.
- `Payments`: cash payment source fact and correction/cancellation model.
- `Freezes`: one add-freeze command to prove source-driven effective end date recalculation.
- `Reports`: daily cash/visits report with drill-down to source rows and audit/history links.
- `Audit`: append-only business audit for every successful state-changing command.
- `Technical logs`: structured command logs with `request_correlation_id`; not a substitute for audit.

UI screens:

- Reception dashboard with account/session/device indicator.
- Search island/result area with exact card auto-open behavior.
- Client profile with active membership panel, warnings and recent history.
- Issue membership form backed by membership type query and preview.
- Mark visit quick action with warning acknowledgement.
- Add freeze form.
- Correction forms for cancel visit and correct payment, including reason/comment.
- Daily report screen with payment/visit drill-down and correction/cancellation rows.
- Audit/history timeline reachable from profile or report drill-down.

UI visual/layout patterns for these screens follow `docs/ui-design-foundation.md`; the slice should prove tablet/phone usability and component consistency, not broad visual polish.

Commands in scope:

- `IssueMembership` with optional cash payment inside the workflow.
- `MarkVisit`.
- `CancelVisit`.
- `CorrectPayment`.
- `AddFreeze`.

Queries in scope:

- `SearchClients`.
- `GetClientProfile`.
- `GetMembershipTypesForIssue`.
- `PreviewIssueMembership`.
- `GetMembershipState`, directly or through `GetClientProfile`.
- `GenerateDailyReport`.
- `GetClientHistory` or equivalent profile history read.
- `GetAuditTimeline`.

Data records in scope:

- `accounts`, `sessions`.
- `clients`, `client_card_assignments`.
- `membership_types`.
- `issued_memberships` with issue-time snapshot fields.
- `membership_state_cache`.
- `membership_extension_days`.
- `visits`, `visit_consumptions`, `visit_cancellations`.
- `payments`, `payment_corrections` and replacement/cancellation rows as required by the chosen correction model.
- `freezes`.
- `business_audit_entries`.
- idempotency/duplicate-submit storage for quick actions.

Audit events in scope:

- `membership.issued`.
- `payment.created`.
- `freeze.added`.
- `visit.marked`.
- `visit.canceled`.
- `payment.corrected` or `payment.canceled`, depending on correction mode.

Each audit entry must include actor/account, role, session/device when available, action type, entity refs, related ids, `entry_origin`, `occurred_at`, `recorded_at`, before/after or domain summary, reason/comment where required, request correlation id and idempotency key when applicable.

## 4. Out of scope

The slice intentionally excludes:

- full Client create/update UI, duplicate warning workflow and card reassignment UI;
- MembershipType create/edit/deactivate screens;
- full owner settings area;
- NonWorkingDay preview/add/correct/cancel workflow;
- freeze cancellation, except where domain tests need it later;
- membership cancellation/correction beyond visit/payment correction;
- negative closure by new membership or one-off closure;
- opening state/manual backfill UI;
- full paper fallback batch UI and reconciliation workflow;
- day close/reconciliation command;
- ending-soon, low-remaining, negative-clients and inactive-clients report screens;
- exported report snapshots;
- app-level backup/export UI;
- restore rehearsal execution;
- online payments, POS, bank integrations and complex accounting;
- client portal, client accounts, public API, QR/NFC/turnstile identity, native mobile app, offline-first sync, multi-tenant/SaaS scope;
- full Excel/paper import and migration tooling;
- broad visual design polish beyond tablet/phone usability for the included screens.

One narrow technical proof for `occurred_at` vs `recorded_at` may be covered in command/application tests, but the complete paper fallback workflow stays out of this slice.

## 5. Technical flow

1. Bootstrap data
   - Seed or migration/setup creates Owner, shared Reception/Admin account, active session, active MembershipType and one existing Client with a current card.
   - This keeps the slice focused on reception architecture instead of generic CRUD.

2. Search and profile read
   - UI calls `SearchClients` with card `BL-1001`.
   - Exact current card match returns one `auto_open_client_id`.
   - UI calls `GetClientProfile` and renders identity, current card, empty/current membership state, warnings and allowed actions.

3. Issue membership with cash payment
   - UI calls `GetMembershipTypesForIssue` and `PreviewIssueMembership`.
   - `IssueMembership` runs in one transaction.
   - The command validates actor, client, active membership type, start date, payment amount and idempotency key.
   - It creates `issued_memberships`, copies snapshot fields, creates the payment source fact, initializes/recalculates `membership_state_cache`, appends `membership.issued` and `payment.created` audit entries, and commits.
   - After success, UI rereads `GetClientProfile`; it does not apply local formulas.

4. Add freeze and recalculate effective end date
   - `AddFreeze` locks Membership first, verifies ADR-015 lifecycle/start eligibility
     and absence of active counted Membership Visit overlap, then creates a
     `freezes` source fact with the full inclusive range and reason.
   - An eligible Freeze end may cross the pre-command effective end and is not clipped.
   - Memberships recalculates extension source days, rebuilds `membership_extension_days`, updates `extension_days` and `effective_end_date`, and includes before/after membership summary in audit.
   - Profile shows the extension explanation from Memberships state.

5. Mark visits and enter negative state
   - Each `MarkVisit` creates `visits` and `visit_consumptions`, recalculates the selected membership and appends `visit.marked`.
   - The third visit requires warning acknowledgement because remaining visits is `0`.
   - Memberships sets signed `remaining_visits = -1`, `negative_balance = 1` and `first_negative_visit_date = 2026-07-03`.
   - Daily report sees visits through canonical visit rows after commit.

6. Cancel mistaken visit
   - `CancelVisit` requires reason/comment and idempotency key.
   - It creates `visit_cancellations`, marks visit/consumption inactive or canceled according to the data model, recalculates the membership, appends `visit.canceled`, and commits.
   - Profile reread shows remaining visits `0` and no negative state.
   - Daily report excludes the canceled visit from active totals while keeping the row visible in drill-down/history.

7. Correct payment
   - `CorrectPayment` requires reason/comment.
   - It preserves the original payment, creates correction/replacement records, appends `payment.corrected` or `payment.canceled`, and commits.
   - Membership state is not recalculated unless the selected payment correction policy makes it membership-state relevant.
   - Daily report cash total is recomputed from active canonical payment rows.

8. Report and audit consistency
   - `GenerateDailyReport` reads canonical visits/payments and Memberships public state/read model for membership summaries only.
   - Report totals must equal drill-down rows:
     - canceled visits excluded from active visit count;
     - corrected/canceled payments excluded or replaced according to canonical payment status;
     - correction/cancellation rows remain visible.
   - `GetAuditTimeline` explains successful commands and corrections but is not used to compute totals.
   - Technical logs share `request_correlation_id` with audit for debugging, but logs are not business history.

## 6. Test plan

Domain tests:

- inclusive membership date arithmetic: `start_date + duration_days - 1 day`;
- issue-time MembershipType snapshot remains immutable after catalog changes in test setup;
- remaining visits from counted visits;
- zero-to-negative visit transition with first negative date;
- canceling the negative visit clears or moves negative state;
- freeze inclusive range contributes expected extension days;
- freeze start before Membership start or after locked pre-command effective end is rejected;
- eligible freeze end beyond pre-command effective end is stored and counted in full;
- active counted Membership Visit overlap returns `freeze_conflicts_with_visit`;
- canceled visit excluded from counted visits, negative state and last-counted visit logic;
- direct effective end date edit is impossible outside source facts or explicit audited adjustment.

Application command tests:

- each command validates permissions, idempotency key, stale/concurrency behavior and required reason/comment;
- successful `IssueMembership`, `AddFreeze`, `MarkVisit`, `CancelVisit` and `CorrectPayment` commit source fact, recalculation and audit together;
- forced recalculation/audit failure rolls back the whole command;
- duplicate quick-action submit returns duplicate/idempotent result without creating a second visit/payment/freeze;
- `entry_origin`, `occurred_at` and `recorded_at` are accepted and persisted correctly for one backdated command test, without building the full paper fallback UI.

Persistence and migration tests:

- run against PostgreSQL, not SQLite or EF InMemory;
- foreign keys, not-null/check constraints and partial unique current-card indexes work;
- payment amount and inclusive date range constraints work;
- one active/current source rule is enforced where the schema requires it;
- `membership_state_cache` can be rebuilt from source records and compared to stored cache for the scenario.

Report consistency tests:

- daily visit count excludes canceled visits and equals active visit drill-down rows;
- daily payment count/cash sum reflects corrected payment state and equals payment drill-down rows;
- client profile and daily report read the same Memberships state;
- report drill-down links expose original fact, correction/cancellation fact and audit entry;
- report query does not contain independent remaining-visits, negative-balance or effective-end-date formulas.

UI tests:

- Playwright tablet smoke: search exact card, open profile, issue membership, add freeze, mark visits, acknowledge negative warning, cancel visit, correct payment, open daily report;
- phone-width smoke: critical warnings/actions remain reachable in order;
- quick-action buttons enter busy/disabled state and do not double-submit;
- after each successful command the UI rereads canonical state and displays server-provided warnings.

Operational checks:

- structured technical log exists for each command with correlation id, command name, duration and outcome;
- business audit entry exists separately from logs;
- sensitive data is not copied into technical logs unnecessarily;
- recalculation failure is treated as a failed command, not a partial success.

## 7. Acceptance criteria

The slice is accepted when all of the following are true:

- Reception can complete the scenario from search to report without direct database edits or manual state patching.
- `IssueMembership` copies snapshot values and initializes membership state correctly.
- `AddFreeze` changes effective end date only through source fact driven recalculation.
- `MarkVisit` can take remaining visits to `0` and then `-1` only through explicit warning acknowledgement.
- `CancelVisit` preserves history, recalculates membership state and removes the visit from active daily totals.
- `CorrectPayment` preserves payment history and updates daily cash totals through canonical payment records.
- Every successful state-changing command creates append-only business audit with required actor/session/time/reason/correlation fields.
- Client profile, Memberships query and daily report agree on remaining visits, negative state and effective end date.
- Daily report totals exactly match drill-down source rows.
- Reports do not duplicate Memberships formulas.
- UI performs canonical reread after command success and does not keep optimistic business values as truth.
- Idempotency prevents duplicate visits/payments/freezes from double taps or repeated submits.
- PostgreSQL-backed tests cover the transaction and constraint behavior needed by the slice.
- The team can use audit entry plus technical correlation id to debug one failed or corrected command.
- The result is enough to decide whether to continue full BodyLife CRM v1 implementation with the selected stack and module boundaries.

## 8. Risks

- Scope creep: adding full CRUD, all report screens, NonWorkingDay workflow or import/fallback UI would turn the slice into the whole product.
- Formula drift: Reports or UI may accidentally duplicate Memberships calculations for convenience.
- Transaction complexity: source facts, recalculation and audit may be harder to keep atomic than expected.
- EF/Core migration gaps: PostgreSQL partial indexes, checks or locking may need reviewed SQL earlier than planned.
- Correction semantics: payment replacement/cancellation and visit cancellation must stay explainable without silent history rewrites.
- Audit noise vs audit gaps: too much audit becomes unreadable, but missing before/after summaries makes disputes hard to resolve.
- htmx interaction risk: partial refreshes must not leave stale membership values after command success.
- Visit implementation may regress ADR-014 explicit allocation under ambiguous Memberships; one-off negative closure remains a separate unresolved/deferred policy.
- Backdated metadata can be technically stored in the slice, but real paper fallback reconciliation still needs a later workflow.
- Passing the slice does not equal production readiness; backup/restore rehearsal, hosting monitoring, full paper fallback process and owner operations checklist remain separate gates before production use.
