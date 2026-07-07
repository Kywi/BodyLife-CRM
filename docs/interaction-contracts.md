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
- `occurred_at` або business date/range, якщо command створює бізнес-факт;
- `recorded_at` встановлюється сервером на момент успішного commit;
- `reason` або `comment`, коли command є correction, cancellation, backdated/fallback entry, card reassignment або owner-sensitive action.

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
- `day_closed_requires_owner`;
- `membership_not_eligible`;
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
- Input: name, duration_days, visits_limit, price, optional comment, optional active flag defaulting to active, common command envelope.
- Validation: name is present; duration_days > 0; visits_limit >= 0; price >= 0; duplicate active name may warn or block by product policy; no issued membership is created here.
- Permissions: Owner-only.
- Transaction boundary: one ACID transaction creates `membership_types` and audit entry.
- Affected modules: MembershipTypes, Audit, Users/Roles.
- Recalculation: none. Existing issued memberships are not affected.
- Audit event: `membership_type.created`; include full catalog summary.
- Possible errors: `permission_denied`, `validation_failed`, `duplicate_submission`, `concurrency_conflict`.
- UI result: catalog list refreshes; active type becomes available in `IssueMembership` flow.

### EditMembershipType

- Purpose: змінити future catalog values of a MembershipType without changing already issued memberships.
- Input: membership type id, new name, duration_days, visits_limit, price, comment, reason/comment for meaningful business change, common command envelope.
- Validation: type exists; no hard delete; duration_days > 0; visits_limit >= 0; price >= 0; edits do not mutate issued membership snapshots; deactivation uses `DeactivateMembershipType`.
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
- UI result: inactive type disappears from ordinary issue-membership selector but remains visible in catalog/history/report filters.

### IssueMembership

- Purpose: видати конкретний Membership клієнту з immutable snapshot of MembershipType and optional cash payment in the same workflow.
- Input: client id, active membership type id, start date, optional comment, optional payment amount/context, negative balance handling decision, optional manual_backfill/opening-state fields when entry_origin requires it, common command envelope.
- Validation: client exists; membership type exists and is active for ordinary sale; snapshot values are copied from MembershipType at issue time; start date is valid; base end date follows inclusive rule; optional payment amount > 0 and method is cash; if client has negative balance, UI/command must carry explicit decision: leave negative visible, cover by new membership from first negative visit date, or record explicit negative closure; manual backfill/opening state requires reason/source.
- Permissions: Admin + Owner.
- Transaction boundary: one ACID transaction creates `issued_memberships`, optional payment/negative closure/opening state facts, initial `membership_state_cache`, extension-day derived rows if relevant, and audit entries. Lock client and affected memberships when negative closure or coverage is involved.
- Affected modules: Clients, MembershipTypes, Memberships, Payments if payment included, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation for the new membership and any source/covering membership involved in negative closure. Reports read updated canonical facts after commit.
- Audit event: `membership.issued`; plus `payment.created` and/or `membership_negative_closure.created` if those source facts are part of the workflow. Include snapshot, start date, payment summary, negative decision, actor/session.
- Possible errors: `permission_denied`, `not_found`, `membership_type_inactive`, `validation_failed`, `membership_not_eligible`, `negative_decision_required`, `duplicate_submission`, `recalculation_failed`, `concurrency_conflict`.
- UI result: client profile reopens with new membership state, warnings, payment status if created, and history entries. If negative balance remains, UI keeps negative warning visible.

### MarkVisit

- Purpose: зафіксувати Visit and, for membership visit, consume one counted visit from selected Membership.
- Input: client id, visit kind, membership id or one-off/trial context, occurred_at/business date, optional comment, confirmation flags for zero/negative/expired states, common command envelope.
- Validation: client exists; selected membership belongs to client; visit kind is valid; if membership has 0 remaining visits or is expired by date, command may proceed only with explicit warning acknowledgement because negative visits are allowed; at most one active counted consumption per visit; backdated/paper fallback entries require reason/comment.
- Permissions: Admin + Owner.
- Transaction boundary: one ACID transaction creates `visits`, active `visit_consumptions` for membership visit, recalculates affected membership, and appends audit. Lock selected membership state/source rows for counted membership visit.
- Affected modules: Clients, Visits, Memberships, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation of selected membership: counted visits, remaining visits, negative balance, first negative visit date, last counted visit, warnings. Daily report reads the visit after commit.
- Audit event: `visit.marked`; include client, membership/visit kind, occurred_at, before/after membership summary when counted, warning acknowledgement.
- Possible errors: `permission_denied`, `not_found`, `validation_failed`, `membership_not_eligible`, `warning_acknowledgement_required`, `duplicate_submission`, `recalculation_failed`, `concurrency_conflict`.
- UI result: profile membership panel refreshes with new remaining/negative state; daily visit count can update; if state becomes negative, show first negative visit date and warning.

### CancelVisit

- Purpose: скасувати mistaken Visit without deleting history and remove it from counted visits/reports.
- Input: visit id, reason/comment, common command envelope.
- Validation: visit exists; not already canceled; reason/comment required; if visit belongs to closed/reconciled business day, correction follows day-close permission policy; cancellation must deactivate related counted consumption in same transaction.
- Permissions: Admin + Owner for current-day/open-day cancellation; after day close/reconciliation Owner-only or explicit owner-approved policy.
- Transaction boundary: one ACID transaction creates `visit_cancellations`, updates visit/consumption status, recalculates affected membership, and appends audit.
- Affected modules: Visits, Memberships, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation of membership referenced by active consumption; may clear/move first negative visit date and update daily report totals.
- Audit event: `visit.canceled`; include visit summary, reason, before/after membership summary, changed-after-close marker if relevant.
- Possible errors: `permission_denied`, `not_found`, `already_canceled`, `reason_required`, `day_closed_requires_owner`, `recalculation_failed`, `concurrency_conflict`.
- UI result: visit remains visible as canceled in history; membership state and daily report totals refresh; UI labels changed-after-close where applicable.

### CreatePayment

- Purpose: зафіксувати cash Payment for membership sale, one-off/trial, negative closure or other v1 cash context.
- Input: client id, optional membership id, amount, currency, payment context, occurred_at/business date, comment, common command envelope.
- Validation: client exists; membership, if provided, belongs to client; amount > 0; method is cash in v1; payment context is valid; backdated/paper fallback entries require reason/comment and entry_origin marker.
- Permissions: Admin + Owner.
- Transaction boundary: one ACID transaction creates `payments`, optional negative closure source facts if selected by workflow, recalculates affected memberships only when payment participates in membership issue or negative closure policy, and appends audit.
- Affected modules: Payments, Clients, Memberships when linked to issue/negative closure, Reports, Audit, Users/Roles.
- Recalculation: none for ordinary standalone cash payment unless tied to issue workflow, negative closure or explicit correction policy. Daily cash report reads canonical payment rows after commit.
- Audit event: `payment.created`; include amount, context, client/membership, occurred_at, actor/session.
- Possible errors: `permission_denied`, `not_found`, `validation_failed`, `duplicate_submission`, `membership_not_eligible`, `concurrency_conflict`.
- UI result: payment appears in client history and selected day's daily cash report; membership panel refreshes if payment was part of issue/negative closure workflow.

### CorrectPayment

- Purpose: explicitly correct or cancel a cash Payment while preserving business history.
- Input: original payment id, correction mode (`replace` or `cancel`), replacement amount/date/context/comment when replacing, reason/comment, common command envelope.
- Validation: original payment exists; original is not already canceled/replaced unless idempotent repeat; reason required; replacement amount > 0 and method remains cash; replacement membership/client context is valid; closed/reconciled day follows owner policy; old and new occurred dates remain explainable.
- Permissions: Admin + Owner for current-day/open-day correction; after day close/reconciliation Owner-only or explicit owner-approved policy.
- Transaction boundary: one ACID transaction creates cancellation/correction fact, optionally creates replacement `payments` row, marks original status, recalculates affected memberships if payment participates in issue/negative closure policy, and appends audit.
- Affected modules: Payments, Memberships if linked to issue/negative closure, Reports, Audit, Users/Roles.
- Recalculation: daily report totals change through canonical payment status/replacement rows. Membership recalculation only when correction changes a payment that has membership-state consequences.
- Audit event: `payment.corrected` або `payment.canceled`; include before/after payment summary, reason, changed-after-close marker if relevant.
- Possible errors: `permission_denied`, `not_found`, `already_canceled`, `reason_required`, `day_closed_requires_owner`, `validation_failed`, `recalculation_failed`, `concurrency_conflict`.
- UI result: client history shows original and correction/replacement; daily report live totals refresh and drill-down shows why totals changed.

### AddFreeze

- Purpose: додати individual Freeze source range that extends one issued Membership.
- Input: client id, membership id, start date, end date, reason/comment, occurred_at if business event date differs from recorded date, common command envelope.
- Validation: client and membership exist and match; date range is inclusive and `start_date <= end_date`; reason/comment required; backdated/paper fallback entry requires marker; effective end date is not edited directly; overlap with NonWorkingDay is allowed but counted by union calendar-day rule in Memberships.
- Permissions: Admin + Owner.
- Transaction boundary: one ACID transaction creates `freezes`, recalculates affected membership extension days/state, and appends audit.
- Affected modules: Freezes, Memberships, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation of affected membership: extension source days, effective end date, warnings. `membership_extension_days` is rebuilt/explained from source facts.
- Audit event: `freeze.added`; include range, day count, reason, before/after membership effective end date summary.
- Possible errors: `permission_denied`, `not_found`, `validation_failed`, `duplicate_submission`, `recalculation_failed`, `concurrency_conflict`.
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
- Validation: Owner confirms affected membership summary before commit; inclusive date range and `start_date <= end_date`; reason required; overlapping non-working periods warn and must not double-count extension days; affected scope is captured in `non_working_period_applications`.
- Permissions: Owner-only.
- Transaction boundary: one ACID transaction creates `non_working_periods`, captures affected membership application rows, recalculates affected memberships, and appends audit. For v1 scale this should commit atomically; if a future batch path is introduced, the UI must not report complete until recalculation status is consistent and retryable.
- Affected modules: NonWorkingDays, Memberships, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation for all affected memberships: extension source days, effective end date, warnings. Freeze/non-working overlap counted by union calendar days.
- Audit event: `non_working_day.added`; include period, reason, affected membership count/summary, recalculation summary.
- Possible errors: `permission_denied`, `validation_failed`, `preview_expired`, `affected_scope_changed`, `recalculation_failed`, `concurrency_conflict`.
- UI result: owner sees confirmed affected count, recalculation result, and link to affected membership drill-down; client profiles show non-working extension reason.

### CorrectNonWorkingDay

- Purpose: explicitly correct or cancel a non-working period while keeping add/correct history explainable.
- Input: non-working period id, correction mode (`replace_range`, `replace_reason`, `cancel`), replacement start/end/reason when applicable, reason/comment for correction, preview/confirmation token for old/new affected scope, common command envelope.
- Validation: period exists; not already canceled unless idempotent repeat; reason/comment required; replacement range is inclusive and valid; Owner confirms affected memberships before commit; correction must preserve original source record and add correction/cancellation facts instead of hard delete.
- Permissions: Owner-only.
- Transaction boundary: one ACID transaction creates correction/cancellation fact, updates active status or creates replacement period/application rows, recalculates old and new affected membership scopes, and appends audit.
- Affected modules: NonWorkingDays, Memberships, Reports, Audit, Users/Roles.
- Recalculation: synchronous recalculation of every membership in old affected scope and new affected scope. Extension days remain union calendar days across active freeze/non-working/adjustment sources.
- Audit event: `non_working_day.corrected` або `non_working_day.canceled`; include before/after period summary, affected counts, reason, recalculation summary.
- Possible errors: `permission_denied`, `not_found`, `already_canceled`, `reason_required`, `preview_expired`, `affected_scope_changed`, `recalculation_failed`, `concurrency_conflict`.
- UI result: owner sees corrected period, affected membership count, and recalculation result; reports/profile histories remain explainable through original and correction records.

## 5. Queries and read actions

Queries do not mutate business state. They may be technically logged for debugging/access monitoring, but they do not create business audit entries unless a future owner policy explicitly requires report-access auditing.

Query access uses the same actor/session context as commands. Reception/profile/search/daily-report reads are available to Admin + Owner. Owner-only catalog or operational views must still enforce Owner-only policy. Future client self-service must use separate client-safe queries and is outside v1.

### SearchClients

- Input: search text, optional search mode (`auto`, `card`, `name`, `phone`, `last4`), include inactive flag, limit/page cursor.
- Output shape: list of compact client results with client id, display name, phone display, current card number, operational status, match type, match confidence/priority, current membership summary from Memberships query, warnings; optional `auto_open_client_id` only when exact current card match is unique.
- Source modules: Clients/Search for normalized identifiers and current card; Memberships for compact membership state; Audit is not read for ordinary search.
- Consistency expectations: search index/current card state updates in the same transaction as client/card commands; exact current card match is canonical; non-unique or partial matches return a list, never auto-open.

### GetClientProfile

- Input: client id, optional as_of date for membership warnings, include history/drill-down flags.
- Output shape: client identity, current card, operational status, membership timeline, current membership state, warnings, recent visits, payments, freezes, non-working applications, audit/history summaries, allowed quick actions for actor.
- Source modules: Clients, Memberships, Visits, Payments, Freezes, NonWorkingDays, Audit, Users/Roles.
- Consistency expectations: profile reads committed source facts and Memberships public state. UI must use this query after each successful command instead of applying client-side business formulas.

### GetMembershipTypesForIssue

- Input: actor context, optional include inactive flag for owner/catalog screens.
- Output shape: active MembershipType options for ordinary issue flow with name, duration_days, visits_limit, price, comment; inactive types only when requested in owner/catalog context.
- Source modules: MembershipTypes.
- Consistency expectations: ordinary issue flow shows only active types; issued memberships later use copied snapshots, not live mutable catalog values.

### GetMembershipState

- Input: membership id or client id/current-membership selector, as_of date.
- Output shape: snapshot fields, start/base/effective end dates, counted visits, remaining visits, negative balance, first negative visit date/id, extension days, extension explanation rows, last counted visit, warnings.
- Source modules: Memberships; source drill-down may read Visits, Freezes, NonWorkingDays and adjustments.
- Consistency expectations: this is the canonical membership state read. Reports, profile and UI warnings must use this state and must not duplicate formulas.

### PreviewIssueMembership

- Input: client id, membership type id, proposed start date, optional negative handling choice.
- Output shape: issue snapshot preview, base end date, expected initial state, existing negative balance warning, first negative visit date, possible negative-closure options, permission result.
- Source modules: Clients, MembershipTypes, Memberships, Users/Roles.
- Consistency expectations: preview is advisory; `IssueMembership` revalidates all rules in transaction.

### PreviewNonWorkingDayImpact

- Input: proposed start date/end date, reason code, actor context.
- Output shape: affected membership count, affected client/membership compact list or sample/page, overlap warnings, estimated extension changes, confirmation token with expiry.
- Source modules: NonWorkingDays, Memberships.
- Consistency expectations: preview is not source of truth; `AddNonWorkingDay` or `CorrectNonWorkingDay` revalidates affected scope in transaction and may fail with `affected_scope_changed`.

### GenerateDailyReport

- Input: business date, include drill-down flag, include changed-after-close labels, optional filters for actor/report view.
- Output shape: business date; daily visit count; payment count; daily cash sum; visit drill-down rows; payment drill-down rows; cancellation/correction rows; day reconciliation status if present; changed-after-close markers; links to audit/history for each row.
- Source modules: Reports, Visits, Payments, Memberships for membership summaries only, Audit for explanation/drill-down, Users/Roles for permission.
- Consistency expectations: live direct query over canonical source records. Canceled visits/payments are excluded from totals. Corrections after close change live totals but are visible through drill-down/audit. Report must not compute remaining visits, active status, negative balance or end dates itself.

### ListEndingSoonMemberships

- Input: as_of date, threshold default 7 days, pagination/filter options.
- Output shape: memberships with client summary, effective_end_date, days_left, remaining visits, warnings, extension explanation link.
- Source modules: Reports over Memberships public state/read model plus Clients.
- Consistency expectations: `days_left` is computed from query date and Memberships effective_end_date; no independent end-date formula in Reports.

### ListLowRemainingMemberships

- Input: as_of date, threshold default `remaining_visits <= 2`, pagination/filter options.
- Output shape: memberships with client summary, remaining visits, visit limit snapshot, counted visits, last counted visit, warnings.
- Source modules: Reports over Memberships public state/read model plus Clients.
- Consistency expectations: remaining visits comes from Memberships state, not report-local counting.

### ListNegativeClients

- Input: as_of date, pagination/filter options.
- Output shape: clients/memberships with negative balance, remaining visits, first negative visit date/id, related negative closure state if any, quick navigation to profile.
- Source modules: Reports over Memberships public state/read model plus Clients and optional closure facts.
- Consistency expectations: negative balance and first negative visit date come from Memberships recalculation. Payment existence alone never hides negative state.

### ListInactiveClients

- Input: as_of date, threshold `14`, `30` or `60` days, include clients with no visits flag, pagination/filter options.
- Output shape: clients with last counted visit date, days inactive, current/last membership summary, operational status, contact/card summary.
- Source modules: Reports over Memberships state or derived client last counted visit summary, Clients, Visits.
- Consistency expectations: canceled visits do not count as last visit. If no visits exist, query labels this separately instead of inventing a date.

### GetClientHistory

- Input: client id, date range, entity filters, pagination.
- Output shape: chronological source facts and corrections: memberships, visits, payments, freezes, non-working applications, opening states, negative closures, audit summaries, entry_origin labels.
- Source modules: Clients, Memberships, Visits, Payments, Freezes, NonWorkingDays, Audit.
- Consistency expectations: history shows source facts and correction/cancellation facts, not silent rewritten state. Backfilled and paper fallback entries show both occurred_at and recorded_at.

### GetAuditTimeline

- Input: entity type/id or client id, date range, action filters, pagination, actor context.
- Output shape: append-only business audit entries with action type, actor/account/session/device, occurred_at, recorded_at, before/after or domain summary, reason/comment, related ids, request correlation id.
- Source modules: Audit, Users/Roles, related modules for display labels.
- Consistency expectations: audit explains successful commands and corrections. Audit is not used to compute report totals.

## 6. Transaction and consistency rules

- Commands that create or change visits, payments, freezes, non-working days, issued memberships, backfill/opening state or corrections must commit source fact, recalculation and audit consistently.
- Recalculation for single-membership commands is synchronous in the same transaction.
- NonWorkingDay commands recalculate affected memberships in the same completed action for v1. If this ever becomes async, the command contract must expose pending/failed/retry state before UI treats the action as complete.
- Reports and profile screens read committed state after command success. UI must not optimistically keep calculated membership values after a state-changing command.
- Idempotency keys are required for fast reception actions that can be double-submitted: `IssueMembership`, `MarkVisit`, `CreatePayment`, `AddFreeze`, and correction/cancellation commands.
- Concurrency conflicts should fail clearly and ask UI to refresh canonical state, not silently overwrite source facts.
- Direct database edits, synthetic fake history and unmarked backdated entries are outside the application contract.

## 7. UI implications

- Reception dashboard can be built from `SearchClients`, `GetClientProfile`, `GetMembershipState`, `GetMembershipTypesForIssue`, `MarkVisit`, `CreatePayment`, `IssueMembership` and `AddFreeze`.
- Owner catalog/settings can be built from membership type commands and catalog queries.
- Daily report screen can be built from `GenerateDailyReport` and drill-down links to client history/audit.
- Non-working day owner workflow needs preview, confirmation and result screens: `PreviewNonWorkingDayImpact`, `AddNonWorkingDay`, `CorrectNonWorkingDay`.
- Destructive/correction actions need confirmation and reason/comment UI.
- UI must show current account/session so shared Reception/Admin accountability is honest.

## 8. Open questions and ADR candidates

- Multiple active issued memberships per client: v1 contracts require explicit membership selection for visit marking, but the product should still settle default selection behavior before implementation.
- Visit during active Freeze: current contracts allow validation/warning policy to be decided in domain tests; a future ADR may choose block, warn or allow.
- Exact one-off/trial model: contracts allow one-off/trial context, but product should choose dedicated workflow, technical client or separate MembershipType before UI build.
- Day close/reconciliation command is not defined here because the requested v1 command list only includes daily report generation. If day close becomes an explicit workflow, add a separate `CloseDailyReconciliation` command with Owner/Admin policy and audit.
