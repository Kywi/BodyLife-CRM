# BodyLife CRM interaction contracts

Дата: 2026-07-07  
Статус: design draft for v1 implementation

Цей документ описує server-side commands/actions і queries для BodyLife CRM v1. Це не implementation code і не остаточний REST/RPC routing design. Контракти визначають, які бізнес-дії існують, які дані вони приймають, які модулі зачіпають, де проходить transaction boundary, які permissions потрібні, що перераховується, який audit створюється і який результат має отримати UI.

Основа: `docs/domain-model.md`, `docs/data-architecture.md`, accepted ADR package у `docs/adr/`.

## 1. Interaction model

BodyLife CRM v1 - internal hosted web app для одного залу. UI є hybrid server-rendered: сторінки, форми, profile і reports рендеряться сервером, а швидкий пошук, попередження і quick actions можуть бути інтерактивними island-компонентами.

Усі state-changing дії виконуються через server-side commands/actions. UI не змінює бізнес-стан локально і після успішної дії перечитує canonical state із сервера.

Recommended interaction pattern для v1:

- server-side commands/actions для state changes;
- server-side query services для reads і reports;
- command/query separation на рівні application layer;
- modular monolith, один deploy і одна transactional database;
- local in-process hooks/events допустимі тільки для audit, recalculation або lightweight read models після успішного command;
- Reports читають canonical source records і Memberships public queries/read models, не дублюючи membership formulas.

## 2. Common command contract

Кожен command приймає спільний operational envelope:

- `actor_account_id`;
- `actor_role`;
- `session_id` і, якщо доступно, `device_label`;
- `request_correlation_id`;
- `idempotency_key` для quick actions з ризиком повторного submit;
- `entry_origin`: `normal`, `manual_backfill`, `paper_fallback` або майбутній `future_import`;
- `entry_batch_row_id` для batch-backed entry; обов'язковий для
  `paper_fallback` і веде до numbered sheet + unique line metadata;
- `occurred_at` або business date/range, якщо command створює бізнес-факт;
- `recorded_at` встановлюється сервером на момент успішного commit;
- `reason` або `comment`, коли command є correction, cancellation, backdated/fallback entry, card reassignment або owner-sensitive action.

`occurred_at` і `recorded_at` є canonical UTC instants. Якщо UI надсилає
`datetime-local`, server трактує його як `Europe/Kyiv` wall time: DST gap є
`validation_failed`, DST overlap обирає перший chronological occurrence
(більший offset). Unsupported min/max instants і DateOnly boundaries
відхиляються до transaction, тому не створюють source, audit або idempotency
success. Date-only domain inputs не отримують implicit UTC conversion. (ADR-017)

Common command result:

- `status`: success або error;
- primary entity id;
- related ids, які UI може перечитати;
- updated client profile summary або redirect target;
- warnings, які лишаються після command;
- audit entry id для owner/admin history, якщо створено business audit;
- changed-after-close marker, якщо command вплинув на вже reconciled day.

Common errors:

- `permission_denied`;
- `validation_failed`;
- `not_found`;
- `duplicate_submission`;
- `stale_state`;
- `card_number_already_current`;
- `duplicate_warning_not_acknowledged`;
- `warning_acknowledgement_required`;
- `day_closed_requires_owner`;
- `membership_not_eligible`;
- `visit_during_freeze`;
- `freeze_conflicts_with_visit`;
- `membership_type_inactive`;
- `already_canceled`;
- `recalculation_failed`;
- `concurrency_conflict`.

## 3. Module boundaries

| Module | Owns | Talks to | Does not own |
|---|---|---|---|
| Clients/Search | Client identity, normalized phone/name/card search, current card assignment, duplicate warnings. | Memberships, Visits, Payments, Audit. | Membership formulas, report totals. |
| MembershipTypes | Catalog values for future sales. | Memberships, Audit, Users/Roles. | Already issued membership values. |
| Memberships | Issued membership snapshot, recalculation, remaining visits, negative balance, first negative visit date, effective end date, extension days, warnings. | Visits, Payments, Freezes, NonWorkingDays, Reports, Audit. | Raw source ownership for visits/payments/freezes/non-working periods. |
| Visits | Visit facts, visit cancellation, visit consumption source facts. | Clients, Memberships, Reports, Audit. | Independent remaining-visit formulas. |
| Payments | Cash payment facts, payment correction/cancellation. | Clients, Memberships, Reports, Audit. | Complex accounting or POS. |
| Freezes | Freeze source ranges and cancellation facts. | Memberships, Reports, Audit. | Direct end-date mutation. |
| NonWorkingDays | Global non-working periods, application scope, correction/cancellation. | Memberships, Reports, Audit. | Per-client freeze rules. |
| Reports | Query/report views and drill-downs. | Visits, Payments, Memberships, Audit. | Source-of-truth formulas. |
| Audit | Append-only business audit. | All commands. | Technical logs, report totals. |
| Users/Roles | Role checks, sessions, accountability. | All commands. | Business entity state. |

## 4. Commands

### CreateClient

- Purpose: створити Client для reception workflow, опційно з current card number.
- Input: surname, name, optional patronymic, phone, optional card number, comment, operational status, duplicate warning acknowledgements, common command envelope.
- Validation: required identity fields; phone normalization; optional card normalization; current card number must be unique; duplicate phone or similar full name creates warning and requires explicit acknowledgement; Client may be created without card number.
- Permissions: Admin + Owner, including shared Reception/Admin account.
- Transaction boundary: one ACID transaction creates `clients`, optional current `client_card_assignments`, duplicate acknowledgement records if provided, search normalized fields/read index, and audit entry.
- Affected modules: Clients/Search, Audit, Users/Roles.
- Recalculation: none for Memberships. Client search index/normalized columns update in the same transaction.
- Audit event: `client.created`; include actor/session, client identity summary, optional card assignment summary, duplicate warning acknowledgement summary.
- Possible errors: `permission_denied`, `validation_failed`, `card_number_already_current`, `duplicate_warning_not_acknowledged`, `duplicate_submission`, `concurrency_conflict`.
- UI result: open newly created client profile with empty membership state, current card if assigned, and available quick actions for issue membership/payment/freeze where applicable.

### UpdateClient

- Purpose: виправити identity/contact/status/comment Client без silent merge.
- Input: client id, editable identity fields, phone, comment, operational status, duplicate warning acknowledgements, common command envelope.
- Validation: client exists; normalized phone is valid; duplicate phone/similar name warning requires acknowledgement; card number changes are not performed through this command and must use `AssignOrChangeCard`.
- Permissions: Admin + Owner.
- Transaction boundary: one ACID transaction updates `clients`, search normalized fields/read index, duplicate acknowledgement records if needed, and audit entry.
- Affected modules: Clients/Search, Audit, Users/Roles.
- Recalculation: none for Memberships. Search index updates in the same transaction.
- Audit event: `client.updated`; include before/after identity summary, status/comment summary, duplicate acknowledgement if any.
- Possible errors: `permission_denied`, `not_found`, `validation_failed`, `duplicate_warning_not_acknowledged`, `stale_state`, `concurrency_conflict`.
- UI result: re-render client profile/header and search result summaries with updated identity data.

### AssignOrChangeCard

- Purpose: призначити, змінити або перевидати current card number для Client як explicit audited action.
- Input: client id, new card number or explicit clear-card intent, reason/comment for change/reassignment, common command envelope.
- Validation: client exists; new card number is normalized and non-empty unless clearing; one client may have at most one current card; one card number may be current for only one client; existing current card number on another client blocks the command in v1; reason required when replacing or clearing an existing card.
- Permissions: Admin + Owner.
- Transaction boundary: one ACID transaction locks the target client/card assignment rows, ends previous current assignment if present, creates new current assignment if provided, updates search index, and appends audit.
- Affected modules: Clients/Search, Audit, Users/Roles.
- Recalculation: none for Memberships. Exact card search state updates in the same transaction.
- Audit event: `card.assigned`, `card.changed` або `card.cleared`; include old/new card summary, actor/session, reason.
- Possible errors: `permission_denied`, `not_found`, `validation_failed`, `card_number_already_current`, `stale_state`, `concurrency_conflict`.
- UI result: client profile shows new current card; search by exact card opens this client after commit.

### CreateMembershipType

- Purpose: створити active catalog type для майбутніх issued memberships.
- Input: name, kind (`ordinary` or `one_off`), duration_days, visits_limit,
  price, optional comment, optional active flag defaulting to active, common
  command envelope.
- Validation: name is present; kind is controlled; duration_days > 0;
  visits_limit >= 0; active type has price > 0; one_off has visits_limit = 1;
  duplicate active name may warn or block by product policy; no issued
  membership is created here.
- Permissions: Owner-only.
- Transaction boundary: one ACID transaction creates `membership_types` and audit entry.
- Affected modules: MembershipTypes, Audit, Users/Roles.
- Recalculation: none. Existing issued memberships are not affected.
- Audit event: `membership_type.created`; include full catalog summary.
- Possible errors: `permission_denied`, `validation_failed`, `duplicate_submission`, `concurrency_conflict`.
- UI result: catalog list refreshes; active type becomes available in `IssueMembership` flow.

### EditMembershipType

- Purpose: змінити future catalog values of a MembershipType without changing already issued memberships.
- Input: membership type id, new name, duration_days, visits_limit, price,
  comment, reason/comment for meaningful business change, common command
  envelope. Kind is read-only.
- Validation: type exists; no hard delete; kind cannot change; duration_days >
  0; visits_limit >= 0; active type has price > 0; one_off has visits_limit =
  1; edits do not mutate issued/closure snapshots; deactivation uses
  `DeactivateMembershipType`.
- Permissions: Owner-only.
- Transaction boundary: one ACID transaction updates `membership_types` future catalog fields and audit entry.
- Affected modules: MembershipTypes, Memberships for future issue flow only, Audit, Users/Roles.
- Recalculation: none. Already issued memberships keep issue-time snapshot.
- Audit event: `membership_type.edited`; include before/after catalog summary and reason/comment.
- Possible errors: `permission_denied`, `not_found`, `validation_failed`, `stale_state`, `concurrency_conflict`.
- UI result: catalog/settings list refreshes; issue flow uses new values only for future memberships.

### DeactivateMembershipType

- Purpose: зняти MembershipType зі звичайного продажу без hard delete.
- Input: membership type id, reason/comment, common command envelope.
- Validation: type exists; not already inactive unless idempotent repeat; hard delete is forbidden; existing issued memberships/history/reports remain readable.
- Permissions: Owner-only.
- Transaction boundary: one ACID transaction marks type inactive/deactivated_at and appends audit.
- Affected modules: MembershipTypes, Memberships issue flow, Reports/history, Audit, Users/Roles.
- Recalculation: none. Already issued memberships keep snapshots and remain valid.
- Audit event: `membership_type.deactivated`; include before/after active state and reason.
- Possible errors: `permission_denied`, `not_found`, `already_inactive`, `stale_state`, `concurrency_conflict`.
- UI result: inactive type disappears from ordinary issue-membership and
  one-off negative-closure selectors but remains visible in
  catalog/history/report filters.

### IssueMembership

- Purpose: видати конкретний Membership клієнту з immutable ordinary MembershipType snapshot and mandatory exact cash sale Payment in the same workflow.
- Input: client id, active ordinary membership type id, start date, signed `PreviewIssueMembership` token, optional comment and common command envelope; payment amount is not staff input. A `paper_fallback` sale also supplies a first-class batch row reference.
- Validation: client exists; type is active `ordinary` with positive price; snapshot values are copied at issue time; start date is valid; base end date follows inclusive rule; created cash Payment equals snapshot price. Under ADR-020, concrete negative Visits are automatically covered oldest-first up to the new snapshot limit; a zero-limit type is ineligible while concrete negatives exist. Unknown opening/backfill remainder is never synthesized and remains visible. The signed preview token is revalidated under locks as `stale_state`. A no-payment historical declaration uses the separate opening-state command, not `IssueMembership`.
- ADR-021 accepted target (pending runtime implementation): preview also binds predecessor id/status/version and shows closure plus concrete/unknown residual effects. Issue creates none→one, atomically closes a zero predecessor, or allocates ADR-020 concrete debt before closing a negative predecessor; positive balance blocks, including expired, future-start, backdated and paper-fallback cases. Closure, new exact sale Payment, allocation, recalculation, audit and idempotency commit together.
- Permissions: Admin + Owner.
- Transaction boundary: one ACID transaction creates a sale-mode
  `issued_memberships` row, its exact sale Payment, any explicit coverage
  facts, predecessor closure/status when applicable, initial
  `membership_state_cache`, extension-day derived rows if relevant, and audit
  entries. Always lock Client and all affected Memberships, including a zero
  predecessor, using the shared ADR-021 hierarchy below.
- Affected modules: Clients, MembershipTypes, Memberships, Payments, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation for the new membership and any source/covering membership involved in negative closure. Reports read updated canonical facts after commit.
- Audit event: `membership.issued`; plus `payment.created` and `membership_negative_closure.created` when automatic coverage allocates Visits. Include snapshot, start date, payment summary, automatic policy, locked covered Visit ids/count, forced start and remainder, actor/session.
- Possible errors: `permission_denied`, `not_found`, `membership_type_inactive`, `validation_failed`, `membership_not_eligible`, `stale_state`, `duplicate_submission`, `recalculation_failed`, `concurrency_conflict`.
- UI result: client profile reopens with new membership state, exact payment status and history entries. If negative balance remains, UI keeps negative warning visible.

### CreateMembershipOpeningState

- Purpose: чесно завести активний історичний абонемент, коли до запуску
  програми повної історії немає; це не продаж.
- Input: Client, ordinary MembershipType або дозволений snapshot, start date,
  opening as-of date, declared remaining/negative state, known
  end/extension state when available, source reference, required reason and
  common command envelope with `manual_backfill`.
- Validation: opening data is internally consistent; origin is
  `manual_backfill`; no active duplicate opening state exists; no Payment
  amount/context is accepted or created.
- ADR-021 invariant: lock Client and its affected Memberships before creating
  this active declaration. An existing active Membership is a conflict and
  rejects the command without writes; manual opening state cannot create a
  second active row or silently close/replace an existing one. No new opening
  reconciliation or lifecycle-correction workflow is introduced here.
- Permissions: Admin + Owner.
- Transaction: creates an immutable `issuance_mode = opening_state` Membership,
  its opening-state fact, derived state and Audit together; exactly zero
  membership_sale Payments must exist.
- Result: reread profile/history with a visible manual-backfill label. The
  command cannot be used to enter a paper-fallback sale, which uses
  `IssueMembership` and its exact Payment.

### MarkVisit

- Purpose: зафіксувати Visit and, for membership visit, consume one counted visit from selected Membership.
- Input: client id; controlled visit kind `membership`, `one_off` or `trial`; explicit membership id only for membership kind; Kyiv local `occurred_at` input normalized to UTC; typed current-state acknowledgements for expired/zero/negative conditions; optional comment; common command envelope.
- Selection: ADR-021 target has one current active Membership, but every membership Visit command still submits explicit `membership_id` and server never infers a historical Membership. A closed Membership is ineligible for new Visit/Freeze actions; its concrete debt remains eligible only for ADR-020/ADR-018 coverage.
- Validation: client exists; selected membership belongs to client and has lifecycle status active; Visit date is not before membership start; future-start/canceled/corrected Membership is ineligible; expired Membership requires explicit expired acknowledgement; 0 or negative remaining visits require their current server-derived acknowledgements; all simultaneous required warning conditions must be acknowledged; one-off/trial rejects membership id and creates no consumption; at most one active counted consumption per visit; backdated/paper fallback entries require reason/comment.
- Freeze policy: an active Freeze whose inclusive range covers the Visit business date blocks membership kind with `visit_during_freeze`; v1 has no override. Actor must correct/cancel Freeze first or explicitly use one-off/trial without membership consumption.
- Permissions: Admin + Owner.
- Transaction boundary: one ACID transaction locks selected membership state/source and relevant Freeze rows, revalidates selection/warnings, creates `visits`, creates active `visit_consumptions` only for membership kind, recalculates affected membership, and appends audit. One-off/trial creates only the Visit/audit facts and no Memberships recalculation.
- Affected modules: Clients, Visits, Memberships, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation of selected membership from ordered active counted Visits (`occurred_at`, `recorded_at`, stable Visit id): counted visits, remaining visits, negative balance, first negative visit/date, last counted visit and warnings. Daily report reads every visit kind after commit.
- Audit event: `visit.marked`; include client, explicit membership or one-off/trial context, occurred_at, before/after membership summary when counted and warning acknowledgements. Historical ambiguity payloads remain readable.
- Possible errors: `permission_denied`, `not_found`, `validation_failed`, `membership_not_eligible`, `visit_during_freeze`, `warning_acknowledgement_required`, `duplicate_submission`, `recalculation_failed`, `concurrency_conflict`.
- UI result: profile membership panel refreshes only for membership kind; daily visit count can update for every kind; if state becomes negative, show first negative visit date and warning. Forms retain the explicit selected context after validation errors; committed current selection is only `none` or `single`.

### CancelVisit

- Purpose: скасувати mistaken Visit without deleting history and remove it from counted visits/reports.
- Input: visit id, reason/comment, common command envelope.
- Validation: visit exists; not already canceled; reason/comment required; if visit belongs to closed/reconciled business day, correction follows day-close permission policy; cancellation must deactivate related counted consumption in same transaction.
- ADR-021 lifecycle dependency: under the shared Client/Membership/source locks,
  Memberships previews the resulting canonical state. If cancellation would
  make a closed non-positive Membership positive, reject before writes with the
  exact lifecycle dependency. A permitted correction preserves `closed`, does
  not transfer visits or reactivate, and keeps historical debt explainable.
- Permissions: Admin + Owner for current-day/open-day cancellation; after day close/reconciliation Owner-only or explicit owner-approved policy.
- Transaction boundary: one ACID transaction creates `visit_cancellations`, updates visit/consumption status, recalculates affected membership, and appends audit.
- Affected modules: Visits, Memberships, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation of membership referenced by active consumption; may clear/move first negative visit date and update daily report totals.
- Audit event: `visit.canceled`; include visit summary, reason, before/after membership summary, changed-after-close marker if relevant.
- Possible errors: `permission_denied`, `not_found`, `already_canceled`, `reason_required`, `day_closed_requires_owner`, `recalculation_failed`, `concurrency_conflict`.
- UI result: visit remains visible as canceled in history; membership state and daily report totals refresh; UI labels changed-after-close where applicable.

### CreatePayment

- Purpose: зафіксувати standalone cash Payment тільки для accepted
  non-membership-sale context, наприклад one-off/trial Visit або інший окремо
  дозволений v1 cash fact.
- Input: client id, amount, currency, accepted standalone payment context, Kyiv
  local `occurred_at` input normalized to UTC, comment, common command envelope.
- Validation: client exists; amount > 0; method is cash in v1; context is
  standalone and valid. `membership_sale` та `negative_closure` rejected:
  перший створює тільки `IssueMembership`, другий — тільки ADR-018 negative
  coverage command. Backdated/paper fallback entries require reason/comment,
  entry origin and first-class batch row reference.
- Permissions: Admin + Owner.
- Transaction boundary: one ACID transaction creates standalone `payments` and appends audit.
- Affected modules: Payments, Clients, Reports, Audit, Users/Roles.
- Recalculation: none. Daily cash report reads canonical payment rows after commit.
- Audit event: `payment.created`; include amount, context, client, occurred_at, actor/session.
- Possible errors: `permission_denied`, `not_found`, `validation_failed`, `duplicate_submission`, `membership_not_eligible`, `concurrency_conflict`.
- UI result: payment appears in client history and selected day's daily cash report; no Membership state changes.

### CorrectPayment

- Purpose: explicitly correct or cancel a cash Payment while preserving business history.
- Input: original payment id, correction mode (`replace` or `cancel`), replacement amount/date/context/comment when replacing, reason/comment, common command envelope.
- Validation: original payment exists; its context is not `membership_sale` or
  `negative_closure`; original is not already canceled/replaced unless
  idempotent repeat; reason required; replacement amount > 0 and method remains
  cash; replacement client/context is valid; command and replacement instants
  satisfy the common Kyiv/UTC boundary; closed/reconciled day follows owner
  policy; old and new occurred dates remain explainable. Sale/closure-linked
  payments reject and direct the actor to their complete ADR-018 workflow.
- Permissions: Admin + Owner for current-day/open-day correction; after day close/reconciliation Owner-only or explicit owner-approved policy.
- Transaction boundary: one ACID transaction creates cancellation/correction fact, optionally creates replacement `payments` row, marks original status and appends audit.
- Affected modules: Payments, Reports, Audit, Users/Roles.
- Recalculation: daily report totals change through canonical payment status/replacement rows; standalone payment correction does not change Membership state.
- Audit event: `payment.corrected` або `payment.canceled`; include before/after payment summary, reason, changed-after-close marker if relevant.
- Possible errors: `permission_denied`, `not_found`, `already_canceled`, `reason_required`, `day_closed_requires_owner`, `validation_failed`, `recalculation_failed`, `concurrency_conflict`.
- UI result: client history shows original and correction/replacement; daily report live totals refresh and drill-down shows why totals changed.

### AddFreeze

- Purpose: додати individual Freeze source range that extends one issued Membership.
- Input: client id, membership id, start date, end date, reason/comment, Kyiv local `occurred_at` normalized to UTC if business event time differs from recorded time, common command envelope.
- Validation: client and membership exist and match; Membership is lifecycle-active;
  date range is inclusive and `start_date <= end_date`; start is on or after
  Membership start and on or before the locked canonical pre-command effective
  end date; end may cross that effective end and the range is not clipped; an
  active counted Membership Visit inside the range is rejected while canceled
  and one_off/trial Visits do not block; reason/comment required; backdated/paper
  fallback entry requires marker; effective end date is not edited directly;
  overlap with another Freeze or NonWorkingDay is allowed and Memberships counts
  the union of unique calendar days.
- Permissions: Admin + Owner.
- Transaction boundary: one ACID transaction locks Membership first, validates
  canonical state and relevant Membership Visits, creates `freezes`, recalculates
  affected membership extension days/state, and appends audit.
- Affected modules: Freezes, Memberships, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation of affected membership: extension source days, effective end date, warnings. `membership_extension_days` is rebuilt/explained from source facts.
- Audit event: `freeze.added`; include range, day count, reason, before/after membership effective end date summary.
- Possible errors: `permission_denied`, `not_found`, `membership_not_eligible`,
  `freeze_conflicts_with_visit`, `validation_failed`, `duplicate_submission`,
  `recalculation_failed`, `concurrency_conflict`.
- UI result: profile history shows freeze; membership panel shows updated effective end date and extension explanation.

### CancelFreeze

- Purpose: cancel mistaken Freeze source range without deleting history.
- Input: freeze id, reason/comment, common command envelope.
- Validation: freeze exists; not already canceled; reason/comment required; closed/reconciled correction follows owner policy if relevant; canceled freeze must contribute zero extension days after recalculation.
- Permissions: Admin + Owner for current-day/open-day cancellation; after day close/reconciliation Owner-only or explicit owner-approved policy.
- Transaction boundary: one ACID transaction creates `freeze_cancellations`, updates freeze status, recalculates affected membership, and appends audit.
- Affected modules: Freezes, Memberships, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation of affected membership extension days/effective end date/warnings.
- Audit event: `freeze.canceled`; include freeze range, reason, before/after membership effective end date summary.
- Possible errors: `permission_denied`, `not_found`, `already_canceled`, `reason_required`, `day_closed_requires_owner`, `recalculation_failed`, `concurrency_conflict`.
- UI result: freeze remains in client history as canceled; membership effective end date and extension explanation refresh.

### AddNonWorkingDay

- Purpose: додати global non-working day/period and apply it to affected memberships with explicit owner confirmation.
- Input: start date, end date, reason code, reason comment, preview/confirmation token or affected-scope acknowledgement, common command envelope.
- Validation: Owner confirms the exact ADR-016 affected Membership set and full applied range before commit; candidate Membership must be lifecycle-active and have any inclusive overlap with canonical pre-command state calculated without the proposed period; inclusive date range and `start_date <= end_date`; reason required; overlapping non-working periods warn and must not double-count extension days; affected scope is captured in `non_working_period_applications`.
- Permissions: Owner-only.
- Transaction boundary: one ACID transaction creates `non_working_periods`, captures affected membership application rows, recalculates affected memberships, and appends audit. For v1 scale this should commit atomically; if a future batch path is introduced, the UI must not report complete until recalculation status is consistent and retryable.
- Affected modules: NonWorkingDays, Memberships, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation for all affected Memberships. Every confirmed application contributes the full period without Membership-boundary clipping; Memberships calculates effective end/warnings and union unique dates across Freeze/non-working sources.
- Audit event: `non_working_day.added`; include period, reason, affected membership count/summary, recalculation summary.
- Possible errors: `permission_denied`, `validation_failed`, `preview_expired`, `affected_scope_changed`, `recalculation_failed`, `concurrency_conflict`.
- UI result: owner sees confirmed affected count, recalculation result, and link to affected membership drill-down; client profiles show non-working extension reason.

### CorrectNonWorkingDay

- Purpose: explicitly correct or cancel a non-working period while keeping add/correct history explainable.
- Input: non-working period id, correction mode (`replace_range`, `replace_reason`, `cancel`), replacement start/end/reason when applicable, reason/comment for correction, preview/confirmation token for old/new affected scope, common command envelope.
- Validation: period exists; not already canceled unless idempotent repeat; reason/comment required; replacement range is inclusive and valid; Owner confirms exact old/new scope before commit; `replace_range` calculates replacement eligibility from canonical state with the old period excluded; `replace_reason` preserves the existing application set/ranges; correction preserves original source/application records instead of hard delete.
- Permissions: Owner-only.
- Transaction boundary: one ACID transaction creates correction/cancellation fact, updates active status or creates replacement period/application rows, recalculates old and new affected membership scopes, and appends audit.
- Affected modules: NonWorkingDays, Memberships, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation of the union of every Membership in retained old scope and confirmed new scope. Extension days remain union calendar days across active freeze/non-working/adjustment sources.
- Audit event: `non_working_day.corrected` або `non_working_day.canceled`; include before/after period summary, affected counts, reason, recalculation summary.
- Possible errors: `permission_denied`, `not_found`, `already_canceled`, `reason_required`, `preview_expired`, `affected_scope_changed`, `recalculation_failed`, `concurrency_conflict`.
- UI result: owner sees corrected period, affected membership count, and recalculation result; reports/profile histories remain explainable through original and correction records.

## 5. Queries and read actions

Queries do not mutate business state. They may be technically logged for debugging/access monitoring, but they do not create business audit entries unless a future owner policy explicitly requires report-access auditing.

Query access uses the same actor/session context as commands. Reception/profile/search/daily-report reads are available to Admin + Owner. Owner-only catalog or operational views must still enforce Owner-only policy. Future client self-service must use separate client-safe queries and is outside v1.

### Reception home read contracts

`GetReceptionAttentionSummary` is a Reports-owned read query. Input is an explicit `ActorContext`, Kyiv `as_of` date and `ending_soon_days_threshold`. Owner, named Admin and shared Reception/Admin are authorized. It returns exact lifecycle-active ending-soon count with the same inclusive `[as_of, as_of + threshold]` effective-end-date rule as `ListEndingSoonMemberships`, plus distinct negative-client count and typed report destination keys. Reports composes the Memberships public exact-count query over fresh canonical `membership_state_cache`; it does not calculate membership state. Status is exactly `success`, `permission_denied`, `validation_failed`, `recalculation_failed` or `source_inconsistent`. Failure has no counts and must never be rendered as zero. The query creates no business audit.

`GetReceptionActivity` is a Reports-owned read query. Input is explicit `ActorContext`, Kyiv `recorded_business_date`, limit 1–20 and a versioned, URL-safe, HMAC integrity-protected opaque cursor binding the requested Kyiv day, fixed sort, UTC `recorded_at` and `business_audit_entries.id`. Cursor signature, version, canonical encoding and date-scope failures are `validation_failed`; the cursor is not an authorization substitute. It filters successful whitelisted client/card, membership issue, visit, payment and freeze audit actions with `BusinessTimeZone.GetUtcDayRange`, ordered by `(recorded_at DESC, id DESC)` and PostgreSQL row-value keyset continuation. Each row exposes a controlled event type, audit/source id, safely resolved client id/display name, canonical UTC occurred/recorded instants, entry origin, correction/cancel/changed-after-close indicator, and only action-approved typed navigable related entities; duplicate acknowledgement/match IDs and unknown JSON fields are not exposed. Memberships supplies `none` or the one current active candidate; history carries closed memberships and their open debt separately. Missing/deleted client, absent required client ref, malformed source, invalid origin, row invariant failure or stale/unavailable Membership state fails the whole query as `source_inconsistent` or `recalculation_failed`; no row is dropped, guessed or fabricated. Backfill remains visible by recorded date even when occurred earlier, and reads create no business audit.

### SearchClients

- Input: search text, optional search mode (`auto`, `card`, `name`, `phone`, `last4`), include inactive flag, limit/page cursor.
- Output shape: list of compact client results with client id, display name, phone display, current card number, operational status, match type, match confidence/priority, current membership summary from Memberships query, warnings; optional `auto_open_client_id` only when exact current card match is unique.
- Source modules: Clients/Search for normalized identifiers and current card; Memberships for compact membership state; Audit is not read for ordinary search.
- Consistency expectations: search index/current card state updates in the same transaction as client/card commands; exact current card match is canonical; non-unique or partial matches return a list, never auto-open.

### GetClientProfile

- Input: client id, optional as_of date for membership warnings, include history/drill-down flags.
- Output shape: client identity, current card, operational status, membership timeline, current membership state, warnings, recent visits, payments, freezes, non-working applications, audit/history summaries, allowed quick actions for actor.
- ADR-021 current selection is `none` or `single`; closed Membership history
  and aggregate open concrete/unknown debt are separate. Closing a predecessor
  preserves its sale Payment status and exposes closure reason/successor.
- Source modules: Clients, Memberships, Visits, Payments, Freezes, NonWorkingDays, Audit, Users/Roles.
- Consistency expectations: profile reads committed source facts and Memberships public state. UI must use this query after each successful command instead of applying client-side business formulas.

### GetMembershipTypesForIssue

- Input: actor context, optional include inactive flag for owner/catalog screens.
- Output shape: active `ordinary` MembershipType options for ordinary issue flow with name, duration_days, visits_limit, price, comment; inactive/one_off types only when explicitly requested in owner/catalog context.
- Source modules: MembershipTypes.
- Consistency expectations: ordinary issue flow shows only active ordinary types; issued memberships later use copied snapshots, not live mutable catalog values.

### GetOneOffTypesForNegativeClosure

- Input: actor context.
- Output shape: every active positive-price `one_off` type with one-visit limit,
  name/duration/price and no preferred/recommended marker.
- Source modules: MembershipTypes.
- Consistency expectations: stale/inactive selection fails in the command;
  closure stores immutable line snapshots.

### GetMembershipState

- Input: membership id or client id/current-membership selector, as_of date.
- Output shape: snapshot fields, start/base/effective end dates, counted visits, remaining visits, negative balance, first negative visit date/id, extension days, extension explanation rows, last counted visit and warnings. Current selector returns `none` or one active Membership; membership-id lookup can return historical `closed` state, which additionally labels closure reason/successor and separates concrete coverable debt from unknown visible/non-coverable remainder.
- Source modules: Memberships; source drill-down may read Visits, Freezes, NonWorkingDays and adjustments.
- Consistency expectations: this is the canonical membership state read. Reports, profile and UI warnings must use this state and must not duplicate formulas.

### PreviewIssueMembership

- Input: client id, ordinary membership type id and proposed start date.
- Output shape: issue snapshot, read-only exact payment, base/effective end, expected initial state, deterministic automatic oldest-first covered Visit count/set, forced start when applicable, concrete and unknown remainder, possibly-expired warning, `CanProceedToIssue`, signed opaque token and permission result. It exposes no method or manual quantity.
- ADR-021 output also identifies the predecessor (or absence), closure reason
  and consequence, positive-balance blocker and residual historical debt. The
  token binds active predecessor id/status/version; submit rejects a changed
  predecessor or stale balance as `stale_state` before mutation.
- Source modules: Clients, MembershipTypes, Memberships, Users/Roles.
- Consistency expectations: preview is advisory; its token binds client/type version/proposed date/negative state/full candidate order plus active predecessor id/status/version. `IssueMembership` locks Client, affected Memberships by stable id, opening/Visit/consumption rows in ADR-020 order, closure/allocation rows and Payment dependencies, then recalculates and revalidates in one transaction.

### PreviewNonWorkingDayImpact

- Input: proposed start date/end date, reason code, actor context.
- Output shape: affected Membership count, exact ordered Membership/Client IDs (with compact list/sample/page for display), full applied range and estimated before/after extension per Membership, overlap warnings, scope fingerprint and confirmation token with expiry.
- Source modules: NonWorkingDays, Memberships.
- Consistency expectations: preview is not source of truth; token binds proposed input plus exact IDs/ranges. `AddNonWorkingDay` or `CorrectNonWorkingDay` revalidates the same ADR-016 policy in a consistent transaction snapshot and fails `preview_expired` or `affected_scope_changed` without partial writes. Successful application rows are an immutable confirmed snapshot; later Membership/source changes do not silently mutate it.

### GenerateDailyReport

- Input: business date, include drill-down flag, include changed-after-close labels, optional filters for actor/report view.
- Output shape: business date; daily visit count; payment count; daily cash sum; visit drill-down rows; payment drill-down rows; cancellation/correction rows; day reconciliation status if present; changed-after-close markers; links to audit/history for each row.
- Source modules: Reports, Visits, Payments, Memberships for membership summaries only, Audit for explanation/drill-down, Users/Roles for permission.
- Consistency expectations: live direct query over canonical source records. Business date maps to the half-open UTC range between consecutive `Europe/Kyiv` midnights, which may be 23, 24 or 25 hours. Canceled visits/payments are excluded from totals. Corrections after close change live totals but are visible through drill-down/audit. Report must not compute remaining visits, active status, negative balance or end dates itself.

### ListEndingSoonMemberships

- Input: as_of date, threshold default 7 days, pagination/filter options.
- Output shape: memberships with client summary, effective_end_date, days_left, remaining visits, warnings, extension explanation link.
- Source modules: Reports over Memberships public state/read model plus Clients.
- Consistency expectations: `days_left` is computed from query date and Memberships effective_end_date; no independent end-date formula in Reports.
- Lifecycle scope: the sole active Membership only; closed history is excluded.

### ListLowRemainingMemberships

- Input: as_of date, threshold default `remaining_visits <= 2`, pagination/filter options.
- Output shape: memberships with client summary, remaining visits, visit limit snapshot, counted visits, last counted visit, warnings.
- Source modules: Reports over Memberships public state/read model plus Clients.
- Consistency expectations: remaining visits comes from Memberships state, not report-local counting.
- Lifecycle scope: the sole active Membership only; closed history is excluded.

### ListNegativeClients

- Input: as_of date, pagination/filter options.
- Output shape: clients/memberships with negative balance, remaining visits, first negative visit date/id, related negative closure state if any, quick navigation to profile.
- Source modules: Reports over Memberships public state/read model plus Clients and optional closure facts.
- Consistency expectations: negative balance and first negative visit date come from Memberships recalculation. Payment existence alone never hides negative state.
- Lifecycle scope: all open debt from active and closed Memberships. Expose
  concrete uncovered Visit debt separately from visible-only unknown opening
  remainder; the resolver and ADR-018/020 candidates must not filter concrete
  Visits by active-only Membership status.

### ListInactiveClients

- Input: as_of date, threshold `14`, `30` or `60` days, include clients with no visits flag, pagination/filter options.
- Output shape: clients with last counted visit date, days inactive, current/last membership summary, operational status, contact/card summary.
- Source modules: Reports over Memberships state or derived client last counted visit summary, Clients, Visits.
- Consistency expectations: canceled visits do not count as last visit. If no visits exist, query labels this separately instead of inventing a date.

### GetClientHistory

- Input: client id, date range, entity filters, pagination.
- Output shape: chronological source facts and corrections: memberships, visits, payments, freezes, non-working applications, opening states, negative closures, audit summaries, entry_origin labels.
- ADR-021 history includes lifecycle closure source, canonical reason, successor
  when present, unchanged sale-Payment status and concrete/unknown open debt,
  separately from the current `none`/`single` operational Membership.
- Source modules: Clients, Memberships, Visits, Payments, Freezes, NonWorkingDays, Audit.
- Consistency expectations: history shows source facts and correction/cancellation facts, not silent rewritten state. Date filters use Kyiv business-day half-open UTC ranges. Backfilled and paper fallback entries show both occurred_at and recorded_at as culture-formatted Kyiv local time.

### GetAuditTimeline

- Input: entity type/id or client id, date range, action filters, pagination, actor context.
- Output shape: append-only business audit entries with action type, actor/account/session/device, occurred_at, recorded_at, before/after or domain summary, reason/comment, related ids, request correlation id.
- Source modules: Audit, Users/Roles, related modules for display labels.
- Consistency expectations: audit explains successful commands and corrections. Date filters use Kyiv business-day half-open UTC ranges; visible instants are culture-formatted Kyiv local time without a timezone suffix. Audit is not used to compute report totals.

## 6. Transaction and consistency rules

- Commands that create or change visits, payments, freezes, non-working days, issued memberships, backfill/opening state or corrections must commit source fact, recalculation and audit consistently.
- Recalculation for single-membership commands is synchronous in the same transaction.
- ADR-021 target transactions lock Client; affected Memberships by stable id;
  opening/Visit/consumption rows in ADR-020 order; closure/allocation rows; and
  Payment dependencies. Issue/full-one-off/dependent corrections commit all
  lifecycle, allocation, recalculation, audit and idempotency facts together.
- NonWorkingDay commands recalculate affected memberships in the same completed action for v1. If this ever becomes async, the command contract must expose pending/failed/retry state before UI treats the action as complete.
- The standalone versioned Membership cache rebuild is a derived-state release/repair operation, not a business command. It commits one Membership at a time, creates no business audit, logs progress/failure, and is safely rerun after partial completion. It does not change the atomic transaction contract of `AddNonWorkingDay` or any user workflow. (ADR-017)
- Reports and profile screens read committed state after command success. UI must not optimistically keep calculated membership values after a state-changing command.
- Idempotency keys are required for fast reception actions that can be double-submitted: `IssueMembership`, `MarkVisit`, `CreatePayment`, `AddFreeze`, and correction/cancellation commands.
- Membership Visit/Freeze commands share a Membership-first lock order. MarkVisit
  locks the Membership before overlapping Freezes; AddFreeze locks the Membership
  before overlapping active counted Membership Visits and before creating its
  source fact.
- Concurrency conflicts should fail clearly and ask UI to refresh canonical state, not silently overwrite source facts.
- Direct database edits, synthetic fake history and unmarked backdated entries are outside the application contract.

## ADR-018 sale, coverage and replacement contract

ADR-021 target dependency rule: cancellation/replacement of an issued sale and
coverage correction must expose and handle their closure/allocation
dependencies through the existing reason-required ADR-018 workflows or reject
without partial state. They do not auto-reactivate a predecessor, transfer
Visits, or introduce an independent lifecycle-correction action.

### PreviewCloseNegativeVisitsOneOff

- Purpose: provide an advisory, Memberships-owned preview for an explicit
  one-off selection without creating closure, Payment, Audit or cache facts.
- Permission and consistency: authorization is checked before detailed input
  validation. The handler reads canonical negative Visits, active one-off
  catalog selectors and exact prices in one read-only PostgreSQL
  `REPEATABLE READ` transaction.
- Output: exact line and cash Payment totals, concrete oldest-first Visits,
  remaining known/unknown negative balance and current stale selectors. No
  option is selected or recommended by the server.
- The preview is not an authorization token. `CloseNegativeVisitsOneOff`
  revalidates permissions, catalog versions, oldest-open Visit state, amounts
  and allocation constraints while holding its command locks.

### PreviewCorrectNegativeVisitCoverage

- Purpose: explain cancel or same-method replacement before changing an active
  one-off or new-Membership closure. Reason and an explicit replacement shape
  are required exactly as for the command.
- Permission and consistency: authorization is checked before detailed input
  validation. Historical closure/items/Payment facts, hypothetical restored
  Membership state, active catalog selectors and replacement amounts are read
  fail-closed in one read-only PostgreSQL `REPEATABLE READ` transaction.
- Output: original and restored concrete Visits, original/replacement exact
  Payment context, covering-Membership restored/replacement capacity,
  resulting known/unknown negative balance and current stale selectors.
- The preview never computes a refund or cash delta and cannot reserve state.
  `CorrectNegativeVisitCoverage` repeats all authorization, lifecycle,
  oldest-first, capacity, price and concurrency checks under command locks.

### CloseNegativeVisitsOneOff

- Purpose: partially or fully close a Client's oldest open negative Visits with
  deliberately selected active one-off catalog types.
- Input: client id, one or more `(membership_type_id, quantity)` lines,
  `occurred_at`, optional normal comment and common command envelope. Payment
  amount is derived from immutable line snapshots, not entered separately.
- Validation: each type is active `one_off`, positive-price and one-visit;
  quantities are positive and total does not exceed open negative count;
  canonical oldest open Visits still match preview; no Visit already has an
  active closure item; line total/currency equals the one created cash Payment.
- Permissions: Admin + Owner.
- Transaction: lock Client, source Memberships and Visits in canonical order;
  create closure, line snapshots, one item per Visit, exact Payment,
  recalculation and Audit, or roll back all.
- ADR-021 extends this to the common lock hierarchy. Partial coverage keeps the
  sole negative Membership active; full coverage bringing its canonical balance
  to zero closes it in the same commit with a lifecycle fact and no successor.
  Concrete debt on already closed Memberships remains eligible oldest-first;
  covering it does not change an unrelated current Membership. Unknown opening
  remainder has no Visit ids and remains visible but non-coverable.
- Result/errors: reread profile, negative report, history and affected daily
  report. Stable errors include inactive/stale type, stale oldest set,
  duplicate coverage, validation, idempotency and concurrency failures.

### New-Membership negative coverage

- ADR-020 makes this automatic within `IssueMembership`, not a staff-selected mode or standalone Payment. Exact Visit ids are the locked oldest-open set; `coverage_count = min(open_concrete_negative_visits, visits_limit_snapshot)`.
- If open negatives exceed the limit, the oldest limit-sized set is covered and the concrete remainder stays visible. A zero-limit type is ineligible if concrete negatives exist; unknown-only historical balance does not block ordinary issue and stays visible.
- The command forces new `start_date` to the business date of the oldest covered
  Visit and atomically creates the new Membership, exact sale Payment and
  allocations. Preview shows covered/remainder counts, new remaining visits,
  end date and already-expired warning.
- Partial coverage is valid; uncovered negative Visits stay visible. A repeated
  or concurrent allocation fails without partial source/audit/cache writes.

### CorrectNegativeVisitCoverage

- Purpose: cancel or replace a mistaken one-off closure without detaching its
  Payment/items.
- Input: original closure id, cancel/replace mode, replacement lines when
  applicable, required reason and common command envelope.
- Permissions: Admin + Owner. Transaction retains the original, cancels its
  active items/Payment, optionally creates the full replacement, recalculates
  source/covering Memberships and updates Audit/reports atomically.
- Under ADR-021 shared locks, preflight every affected Membership. A result
  crossing a closed non-positive balance to positive is an exact lifecycle
  dependency and rejects before writes; allowed correction leaves it closed.
  Closure and allocation dependencies are explicit; no silent reactivation,
  transfer to current Membership or new lifecycle-correction action is allowed.
- No input, output or UI summary contains a calculated refund, extra payment or
  price difference.

### ReplaceIssuedMembership / CancelIssuedMembershipSale

- Purpose: correct a mistaken ordinary sale or cancel it without replacement.
- Input: original Membership id, replacement active ordinary type/start date
  for replace mode, required reason and common command envelope. Payment amount
  is never an input.
- Permissions: Admin, including shared Reception/Admin, and Owner. This remains
  true for an older/closed day; a future close policy adds only a
  changed-after-close marker.
- Preview lists counted Visits, Freezes, NonWorkingDay applications,
  lifecycle closure/successor and negative-coverage links. Command uses the
  shared Client-first hierarchy to lock the sale and dependency set. Every
  transferred effect requires an explicit valid replacement/reallocation fact;
  any blocker, stale set or rule violation rolls back everything.
- Replace marks original Membership corrected/replaced, cancels its sale
  Payment, creates new Membership and exact-price Payment, recalculates and
  appends Audit. Cancel-only cancels original Membership/Payment. Neither
  workflow calculates or displays cash difference/refund.
- Cancel/replacement of a successor does not reactivate its predecessor. A
  closure/allocation dependency must be handled by an accepted explicit
  reason-required workflow or the whole command rejects. Existing sale
  replacement rules do not authorize a new closed-lifecycle correction or
  transfer of positive credit; that requires a separate product decision.
- Result rereads profile, history, Audit and all affected daily reports.

### CreatePaperFallbackBatch / CreatePaperFallbackBatchRow

- Batch input contains one numbered paper sheet and outage/business date range.
  Row input contains or requests a positive line number unique inside the batch,
  event type, actual `occurred_at` and required explanation.
- Permissions: Admin + Owner. Every normal domain command for that paper row
  carries `paper_fallback` origin and the first-class batch-row id; server sets
  `recorded_at`. Duplicate line or mismatched origin fails before business fact.
- Reconciliation rereads all affected daily reports, profiles, history and Audit
  before Admin/Owner marks the batch accepted.

All ADR-018 fast actions require idempotency. Lock order is Client, source
Memberships ordered by id, covering/replacement Membership, affected Visits
ordered by occurrence/id, then sale/closure Payment rows.

## 7. UI implications

- Reception dashboard can be built from `SearchClients`, `GetClientProfile`, `GetMembershipState`, `GetMembershipTypesForIssue`, `MarkVisit`, `CreatePayment`, `IssueMembership` and `AddFreeze`.
- Owner catalog/settings can be built from membership type commands and catalog queries.
- Daily report screen can be built from `GenerateDailyReport` and drill-down links to client history/audit.
- Non-working day owner workflow needs preview, confirmation and result screens: `PreviewNonWorkingDayImpact`, `AddNonWorkingDay`, `CorrectNonWorkingDay`.
- Destructive/correction actions need confirmation and reason/comment UI.
- UI must show current account/session so shared Reception/Admin accountability is honest.
- UI converts canonical instants to `Europe/Kyiv` and formats them through the active culture without `UTC`/offset suffixes. `datetime-local` means Kyiv wall time and DST validation errors remain editable in the originating form. (ADR-017)

## 8. Open questions and ADR candidates

ADR-014 resolves multiple Memberships, Visit allocation, no-active behavior,
one-off/trial context and Visit-during-Freeze policy for v1. ADR-005 resolves
inclusive date arithmetic. ADR-018 resolves exact ordinary sale, oldest-first
partial/full negative coverage, sale replacement/cancel and paper sheet/line
metadata.

- Day close/reconciliation command is not defined here because the requested v1 command list only includes daily report generation. If day close becomes an explicit workflow, add a separate `CloseDailyReconciliation` command with Owner/Admin policy and audit.
