# BodyLife CRM data architecture

## 1. Data architecture summary

BodyLife CRM v1 зберігає бізнесову правду як canonical source facts і будує контрольований derived state для Memberships, пошуку та reports. Цільова форма - relational transactional data model для modular monolith web app: один основний застосунок, одна основна transactional database, чіткі module ownership boundaries і server-side commands.

Основний вибір:

- Source facts зберігаються у звичайних доменних таблицях: clients, card assignments, membership types, issued memberships, visit facts, visit consumptions, payments, freezes, non-working periods, opening states, adjustments, negative closures, day reconciliations.
- Derived state зберігається тільки там, де це дає консистентність або швидкий read: `membership_state_cache`, `membership_extension_days`, пошукові normalized поля. Кожне derived поле має recalculation owner і source records.
- Reports не є джерелом правди. Вони читають canonical facts і Memberships public queries/read models. Кожен total має drill-down до source rows.
- Business audit є окремою append-only business history. Technical logs зберігаються окремо в application/hosting logging stack і не замінюють audit.
- Corrections/cancellations не видаляють бізнес-історію. Вони додають explicit source facts або per-entity cancellation/correction rows, оновлюють active/current marker у тій самій transaction і створюють audit entry.
- Migration v1 стартує зі sterile database. Manual backfill і paper fallback йдуть через ті самі domain commands з `occurred_at`, `recorded_at`, `entry_origin`, reason/comment і audit.
- PostgreSQL timestamp facts зберігають exact UTC instants. Fixed business
  calendar `Europe/Kyiv` використовується на application/query boundaries для
  DateOnly, local-day filters і UI; database/session time zone не визначає
  report semantics. (ADR-017)

Потрібні можливості БД:

- ACID transactions і row-level locking для core workflows.
- Foreign keys, check constraints, not-null constraints, transactional unique constraints.
- Partial/filtered unique indexes або еквівалент для "one active/current row" правил.
- Composite indexes для date reports, membership recalculation і search.
- Functional/generated indexes або application-maintained normalized columns для phone/name/card search.
- JSON/structured payload support для audit summaries допустимий, але ключові поля audit мають бути queryable columns.
- Migrations з reversible/forward-only policy, seed data для roles/settings і restore rehearsal support.
- Managed backup support з retention/PITR або еквівалентом, який дозволяє same-business-day restore.

## 2. Source facts

Source facts - це записи, з яких можна заново пояснити бізнесовий стан без silent patches.

Canonical source facts:

- `clients` - людина, identity data, operational status, notes.
- `client_card_assignments` - current і historical card number assignments.
- `membership_types` - catalog values for future sales.
- `issued_memberships` - конкретний виданий абонемент зі snapshot полями типу на момент видачі.
- `membership_opening_states` - explicit manual opening state для активних абонементів без повної історії.
- `visits` - факт приходу клієнта.
- `visit_consumptions` - факт, який саме membership споживає один counted visit.
- `payments` - факт отримання готівки.
- `freezes` - індивідуальні source ranges для продовження абонемента.
- `non_working_periods` - глобальні закриті дні/періоди залу.
- `non_working_period_applications` - зафіксований scope affected memberships на момент підтвердження period.
- `membership_adjustments` - explicit audited adjustments, наприклад exceptional extension day або виправлення issue-time snapshot.
- `membership_negative_closures` і `membership_negative_closure_items` - явне закриття мінусових visits новим membership або one-off closure workflow.
- `day_reconciliations` - факт звірки/close cash day; не заморожує правду, але потрібен для permissions і changed-after-close labels.
- Per-entity cancellation/correction tables: `visit_cancellations`, `payment_corrections`, `payment_cancellations`, `freeze_cancellations`, `non_working_period_cancellations`, `membership_corrections`.
- `business_audit_entries` - append-only бізнес-історія успішних commands.

Common source fact fields:

- `id`.
- Business identity FK fields, наприклад `client_id`, `membership_id`, `payment_id`.
- `occurred_at` - canonical UTC instant або explicit business `DateOnly`/range,
  якщо entity справді date-based; local wall input спочатку проходить Kyiv DST
  validation.
- `recorded_at` - server-set canonical UTC instant, коли запис внесено в систему.
- `recorded_by_account_id`.
- `recorded_session_id` або device/session reference.
- `entry_origin`: `normal`, `manual_backfill`, `paper_fallback`, `future_import`.
- `entry_batch_id` nullable для fallback/backfill batches.
- `reason` або `comment` nullable/required за command policy.
- `status` або active marker як query convenience, але source truth для скасування/корекції має бути окремий correction/cancellation fact.

## 3. Derived state

Derived state належить Memberships або Clients/Search module. Він не редагується напряму normal workflows.

Membership derived state:

- `base_end_date` - deterministic from `start_date + duration_days_snapshot - 1 day`.
- `counted_visits` - one effective active counted `visit_consumption` per Visit and Membership, where both Visit and effective consumption are active; retained canceled consumption history does not duplicate the Visit.
- `remaining_visits` - signed value: `visits_limit_snapshot - counted_visits + active visit_balance adjustment deltas`, or the honest opening-state declaration plus adjustments recorded after that declaration; negative-closure rules apply when available.
- `negative_balance` - `max(0, -remaining_visits)`.
- `first_negative_visit_date` і optional `first_negative_visit_id` - earliest counted visit that makes running balance negative.
- `extension_days` - count of unique calendar dates from active freezes/applicable non-working periods plus positive active `extension_days` adjustment deltas.
- `effective_end_date` - `base_end_date + extension_days`.
- `last_counted_visit_at` - last active counted visit for inactive-client reports.
- warnings/read flags: ending soon, low remaining, zero visits, negative, expired by date.

Important storage rule: `active_status`, `days_left` і warnings depend on query date. They may be computed at read time from `effective_end_date` and `remaining_visits`, or cached with an explicit `as_of_date` and refreshed daily. They must not become independent business facts.

Recommended derived tables:

| Table | Purpose | Source | Rebuild policy |
|---|---|---|---|
| `membership_state_cache` | One row per issued membership with stable derived values and `recalculation_version`; Kyiv date semantics require version `7`. | `issued_memberships`, `membership_opening_states`, `visit_consumptions`, `visits`, `freezes`, `non_working_periods`, `non_working_period_applications`, `membership_adjustments`, negative closures. | Recalculate synchronously in the same transaction for single-membership commands. A standalone canonical all-cache rebuild is allowed only as a derived-state release/repair operation; it is rerunnable and does not weaken atomic business commands. |
| `membership_extension_days` | Explainable extension day rows. Multiple date-bearing sources may point to the same calendar date; state counts distinct row dates. The aggregate cache total may additionally contain honest opening-state or aggregate adjustment days without reconstructed synthetic dates. | Active freezes, active non-working periods and future adjustment contracts that identify concrete calendar days. A numeric `extension_days` adjustment remains explained by its canonical source row instead of fabricated day rows. | Delete/rebuild for affected memberships during recalculation. It is derived, not source truth. |
| `client_search_index` or stored normalized columns | Fast search by card/name/phone/last 4. | `clients`, current `client_card_assignments`. | Updated in client/card transactions. |

Recalculation trigger matrix:

| Command/fact change | Recalculate |
|---|---|
| Issue/cancel/correct membership | Affected membership and client current membership summary. |
| Record/cancel/correct visit | Membership referenced by active consumption; if consumption allocation changes, old and new memberships. |
| Record/cancel/correct payment | Membership only if payment participates in issue workflow, negative closure or correction policy; daily report reads canonical payment rows. |
| Add/cancel/correct freeze | Affected membership. |
| Add/cancel/correct non-working period | Exact ADR-016 confirmed scope; correction recalculates the union of retained old scope and confirmed replacement scope. Later Membership/source changes do not silently rewrite an existing scope snapshot. |
| Create/update opening state | Affected membership. |
| Add/cancel/correct supported membership adjustment | Affected membership; unsupported active type/delta shape fails recalculation. |
| Negative closure or coverage change | Source membership, covering membership and listed closure visits. |
| Backdated/paper fallback entry | Same as normal command, using `occurred_at` for business calculation and `recorded_at` for audit/report explanation. |

## 4. Proposed schema outline

The outline uses relational table names and column intent, not ORM classes. IDs can be UUIDs or database-generated numeric IDs, but they must be stable across audit and restore.

### Accounts, sessions and roles

| Table | Key fields | Relationships | Notes |
|---|---|---|---|
| `accounts` | `id`, `display_name`, `account_type`, `role`, `is_active`, `created_at` | Referenced by source facts and audit. | `account_type`: `owner`, `named_admin`, `shared_reception_admin`. Do not model client accounts in v1. |
| `sessions` | `id`, `account_id`, `device_label`, `started_at`, `expires_at`, `ended_at`, `last_seen_at` | `account_id -> accounts.id` | Captures session/device for audit. Active sessions require `ended_at is null` and `expires_at > now()`; authenticated activity renews the 12-hour idle expiry. Shared account audit is honest about shared identity. |

### Clients and search

| Table | Key fields | Relationships | Notes |
|---|---|---|---|
| `clients` | `id`, `surname`, `name`, `patronymic`, `normalized_full_name`, `phone_raw`, `phone_normalized`, `phone_last4`, `comment`, `operational_status`, `created_at`, `created_by_account_id`, `updated_at` | Referenced by memberships, visits, payments, audit. | Client may exist without card number. Phone/name duplicates warn but do not block after explicit confirmation. |
| `client_card_assignments` | `id`, `client_id`, `card_number_raw`, `card_number_normalized`, `assigned_at`, `assigned_by_account_id`, `ended_at`, `ended_by_account_id`, `end_reason`, `is_current` | `client_id -> clients.id` | Historical card assignment table. Exactly one current assignment per client and per card number. Card changes require audit. |
| `duplicate_warning_acknowledgements` | `id`, `client_id`, `warning_type`, `matched_client_id`, `acknowledged_by_account_id`, `acknowledged_at`, `reason` | References clients/accounts. | Optional but useful to explain why phone/name duplicate warning was overridden. |

The deterministic card, phone, last-four and name representation used by these fields is defined in `docs/client-search-normalization.md`. Persistence, commands and search queries must reuse that contract rather than maintaining separate normalization formulas.

### Membership catalog and issued memberships

| Table | Key fields | Relationships | Notes |
|---|---|---|---|
| `membership_types` | `id`, `name`, `duration_days`, `visits_limit`, `price_amount`, `price_currency`, `is_active`, `comment`, `created_at`, `updated_at`, `deactivated_at` | Referenced by issued memberships. | Owner-only create/edit/deactivate. No hard delete through app workflows. |
| `issued_memberships` | `id`, `client_id`, `membership_type_id`, `type_name_snapshot`, `duration_days_snapshot`, `visits_limit_snapshot`, `price_amount_snapshot`, `price_currency_snapshot`, `start_date`, `base_end_date`, `issued_at`, `issued_by_account_id`, `status`, `entry_origin`, `entry_batch_id`, `comment` | `client_id -> clients.id`, `membership_type_id -> membership_types.id` | Snapshot fields are immutable after issue except explicit membership correction. `status`: active/canceled/corrected/current marker for queries. |
| `membership_opening_states` | `id`, `membership_id`, `opening_as_of_date`, `declared_remaining_visits`, `declared_negative_balance`, `known_effective_end_date`, `known_extension_days`, `source_reference`, `reason`, `recorded_at`, `recorded_by_account_id`, `status` | `membership_id -> issued_memberships.id` | Source fact for manual backfill when old history is incomplete. At most one active opening state per membership. |
| `membership_adjustments` | `id`, `membership_id`, `adjustment_type`, `days_delta`, `visits_delta`, `money_delta`, `effective_date`, `reason`, `recorded_at`, `recorded_by_account_id`, `recorded_session_id`, `entry_origin`, `entry_batch_id`, `status` | `membership_id -> issued_memberships.id` | Escape hatch for explicit audited corrections. Active v1 calculation accepts only positive day-only `extension_days` and signed non-zero visit-only `visit_balance`; unsupported active money/mixed/unknown shapes fail rebuild. Canceled/corrected history is retained. Prefer domain-specific correction tables when possible. |
| `membership_state_cache` | `membership_id`, `counted_visits`, `remaining_visits`, `negative_balance`, `first_negative_visit_id`, `first_negative_visit_date`, `extension_days`, `effective_end_date`, `last_counted_visit_at`, `recalculated_at`, `recalculation_version` | `membership_id -> issued_memberships.id` | Derived. One row per membership. Rebuildable from source facts. |
| `membership_extension_days` | `id`, `membership_id`, `extension_date`, `source_type`, `source_id`, `source_label`, `is_active`, `recalculated_at` | `membership_id -> issued_memberships.id` | Derived explanation rows. `extension_days` counts distinct active `extension_date`. |

### Visits and visit consumption

| Table | Key fields | Relationships | Notes |
|---|---|---|---|
| `visits` | `id`, `client_id`, `occurred_at`, `recorded_at`, `recorded_by_account_id`, `session_id`, `visit_kind`, `entry_origin`, `entry_batch_id`, `comment`, `status` | `client_id -> clients.id` | Fact of arrival. ADR-014 narrows v1 `visit_kind` to membership, one_off or trial. Cancellation keeps the row visible; one_off/trial has no membership consumption. |
| `visit_consumptions` | `id`, `visit_id`, repeated `client_id`/`visit_kind`, `membership_id`, `consumption_type`, `source_fact_type`, `source_fact_id`, `recorded_at`, `recorded_by_account_id`, `recorded_session_id`, `status` | Composite `(visit_id, client_id, visit_kind) -> visits`; composite `(membership_id, client_id) -> issued_memberships` | Explicit selected membership for membership kind. Composite FKs prove the Visit and Membership belong to the same Client, while a `visit_kind = membership` check makes consumption impossible for one_off/trial. Initial Milestone 6 storage accepts only active/canceled counted consumption sourced by its Visit; later negative-closure/reallocation semantics require an explicit migration. At most one active counted consumption per Visit. Memberships collapses retained rows to one effective Visit source and uses the effective consumption's server `recorded_at` for deterministic ordering and the opening-state recording-time cutover. |
| `visit_cancellations` | `id`, `visit_id`, `reason`, `occurred_at`, `recorded_at`, `recorded_by_account_id`, `session_id`, `entry_origin`, `entry_batch_id` | `visit_id -> visits.id` | Retained source fact with one row per canceled Visit. A future `CancelVisit` transaction updates `visits.status` and related `visit_consumptions.status` without deleting either source row. |

### Payments, freezes and non-working days

| Table | Key fields | Relationships | Notes |
|---|---|---|---|
| `payments` | `id`, `client_id`, `membership_id`, `amount`, `currency`, `method`, `payment_context`, `occurred_at`, `recorded_at`, `recorded_by_account_id`, `session_id`, `entry_origin`, `entry_batch_id`, `comment`, `status` | `client_id -> clients.id`, nullable `membership_id -> issued_memberships.id` | Method v1 is cash. `payment_context`: membership_sale, one_off, trial, negative_closure, other. |
| `payment_cancellations` | `id`, `payment_id`, `reason`, `occurred_at`, `recorded_at`, `recorded_by_account_id`, `session_id` | `payment_id -> payments.id` | Source fact. Daily cash report excludes canceled payment. |
| `payment_corrections` | `id`, `original_payment_id`, `replacement_payment_id`, `changed_fields`, `reason`, `recorded_at`, `recorded_by_account_id`, `session_id` | References `payments` twice. | Replacement row is a new source fact. Old and new occurred dates remain explainable. |
| `freezes` | `id`, `client_id`, `membership_id`, `start_date`, `end_date`, `reason`, `occurred_at`, `recorded_at`, `recorded_by_account_id`, `session_id`, `entry_origin`, `entry_batch_id`, `status` | Composite `(membership_id, client_id) -> issued_memberships(id, client_id)` | Inclusive range. The composite FK proves that the selected Membership belongs to the repeated Client. Active freezes contribute extension dates. |
| `freeze_cancellations` | `id`, `freeze_id`, `reason`, `occurred_at`, `recorded_at`, `recorded_by_account_id`, `session_id`, `entry_origin`, `entry_batch_id` | `freeze_id -> freezes.id` | Retained source fact with at most one cancellation per Freeze. Recalculation removes freeze dates from active extension sources. |
| `non_working_periods` | `id`, `start_date`, `end_date`, `reason_code`, `reason_comment`, `created_at`, `created_by_account_id`, `session_id`, `status` | Global source fact. | Owner-only. Inclusive range. |
| `non_working_period_applications` | `id`, `non_working_period_id`, `membership_id`, `client_id`, `applied_start_date`, `applied_end_date`, `previewed_at`, `confirmed_at`, `status` | References period, membership, client. | Captures the immutable ADR-016 Owner-confirmed scope snapshot. Active applied range equals the full period after any inclusive eligibility overlap; recalculation derives unique union days. |
| `non_working_period_cancellations` | `id`, `non_working_period_id`, `reason`, `recorded_at`, `recorded_by_account_id`, `session_id` | `non_working_period_id -> non_working_periods.id` | Owner-only source fact. Affected memberships recalculate. |

### Negative closure and day reconciliation

| Table | Key fields | Relationships | Notes |
|---|---|---|---|
| `membership_negative_closures` | `id`, `client_id`, `source_membership_id`, `closure_type`, `covering_membership_id`, `payment_id`, `first_negative_visit_date`, `visits_count`, `reason`, `occurred_at`, `recorded_at`, `recorded_by_account_id`, `status` | References client, source membership, optional covering membership/payment. | Explicit workflow that prevents payment/new membership from silently hiding negative visits. |
| `membership_negative_closure_items` | `id`, `negative_closure_id`, `visit_id`, `old_consumption_id`, `new_consumption_id` | References closure, visit and consumptions. | Lists visits covered or reallocated. Recalculation can explain what changed. |
| `day_reconciliations` | `id`, `business_date`, `status`, `closed_at`, `closed_by_account_id`, `expected_cash_sum`, `actual_cash_sum`, `note` | References account. | Minimal cash day close/reconciliation point. Later corrections are allowed by policy and reported as changed after close. |

### Backfill and fallback batches

| Table | Key fields | Relationships | Notes |
|---|---|---|---|
| `entry_batches` | `id`, `batch_type`, `source_label`, `business_date_start`, `business_date_end`, `recorded_at`, `recorded_by_account_id`, `reconciled_at`, `reconciled_by_account_id`, `note` | Referenced by source fact `entry_batch_id`. | `batch_type`: manual_backfill, paper_fallback, future_import. Groups paper/outage entries and active-client backfill. |
| `import_staging_records` | `id`, `batch_id`, `source_row_ref`, `raw_payload`, `validation_status`, `validation_errors`, `created_domain_entity_type`, `created_domain_entity_id` | Optional future import only. | Not needed for v1 UI, but this is the safe future boundary for full Excel import. |

### Audit

| Table | Key fields | Relationships | Notes |
|---|---|---|---|
| `business_audit_entries` | `id`, `action_type`, `entity_type`, `entity_id`, `related_entity_refs`, `account_id`, `account_type`, `role`, `session_id`, `device_label`, `occurred_at`, `recorded_at`, `reason`, `comment`, `before_summary`, `after_summary`, `request_correlation_id`, `entry_origin` | References account/session where possible. Entity references may be polymorphic for cross-module audit. | Append-only. Owner-readable. Not technical logs. |

## 5. Constraints and indexes

Core constraints:

- `clients.phone_normalized` can be nullable, but if present must pass normalization format check.
- `client_card_assignments.card_number_normalized` can be nullable only if the command permits no card; current card rows must have a non-empty normalized number.
- Unique current card assignment: no two rows with `is_current = true` may share `card_number_normalized`.
- Unique current card per client: no client may have more than one row with `is_current = true`.
- `membership_types.duration_days > 0`, `visits_limit >= 0`, `price_amount >= 0`.
- `issued_memberships.duration_days_snapshot > 0`, `visits_limit_snapshot >= 0`, `price_amount_snapshot >= 0`.
- `issued_memberships.base_end_date = start_date + duration_days_snapshot - 1 day` should be enforced by application/domain tests; DB generated column/check is useful if supported.
- No hard delete through application workflows for membership types, issued memberships, visits, payments, freezes, non-working periods and audit.
- Date ranges are inclusive and must satisfy `start_date <= end_date`.
- `payments.method = cash` in v1; keep enum/check narrow until a later ADR expands payment methods.
- `payments.amount > 0`.
- At most one active opening state per membership.
- At most one active counted `visit_consumption` per visit.
- Multiple lifecycle-active issued Memberships per Client are allowed; no uniqueness constraint may silently encode a current Membership.
- Membership-kind Visit requires exactly one active counted consumption after command success; one_off/trial Visit requires none. PostgreSQL prevents any one_off/trial consumption, while the command transaction is responsible for creating the required membership consumption atomically.
- `visit_consumptions` repeats `client_id` and controlled `visit_kind` only to support composite FKs: the selected Membership must belong to the Visit Client and the referenced Visit must be membership kind. These repeated values are relational guards, not independent editable business facts.
- Visit before selected Membership `start_date`, consumption of canceled/corrected Membership, and membership Visit during an active Freeze covering `occurred_at` are rejected under lock by the command/domain boundary; expired selection requires current-state acknowledgement.
- AddFreeze requires a lifecycle-active Membership and validates its inclusive range
  against locked canonical pre-command state: start must be at least Membership
  `start_date` and no later than pre-command `effective_end_date`; end may cross
  that effective end and the stored range is not clipped.
- AddFreeze rejects an active counted Membership Visit inside the proposed range
  with `freeze_conflicts_with_visit`. Canceled Visits and one_off/trial Visits do
  not block it.
- Membership Visit/Freeze concurrency uses one lock order: lock the selected
  `issued_memberships` row first, then read and lock the relevant `freezes` and
  membership Visit rows. MarkVisit, AddFreeze and future CancelFreeze workflows
  keep that order so neither side observes a stale eligibility window.
- NonWorkingDay scope includes only lifecycle-active issued Memberships whose
  locked canonical interval, calculated without the proposed/replaced period,
  has any inclusive overlap. Each stored application uses the full period for
  `applied_start_date..applied_end_date`; range clipping is forbidden.
- At most one active application per non-working period/version and Membership.
  Preview token/fingerprint binds the exact ordered Membership IDs and applied
  ranges. Command revalidation mismatch fails before source/application writes.
- Confirmed application rows are retained snapshots. Period correction/cancel
  changes status through retained facts and recalculates old/new scope; later
  Membership or extension-source changes never UPDATE the confirmed set
  silently.
- `business_audit_entries` are append-only by application policy; database permissions/triggers can harden this if available.

Important indexes:

| Query/workflow | Index |
|---|---|
| Exact card search | Unique/partial index on `client_card_assignments(card_number_normalized)` where `is_current = true`. |
| Open client by current card | Index on `client_card_assignments(client_id)` where `is_current = true`. |
| Phone search | Index on `clients(phone_normalized)`. |
| Last four phone search | Index on `clients(phone_last4)` with optional `operational_status`. |
| Name search | Index on `clients(normalized_full_name)`; add full-text/trigram/fuzzy index only if structured prefix search is not enough. |
| Duplicate warning by phone | Non-unique index on `clients(phone_normalized)`. |
| Client profile memberships | Index on `issued_memberships(client_id, start_date desc, issued_at desc)`. |
| Membership state read | Primary/unique index on `membership_state_cache(membership_id)` and index on `(effective_end_date)`, `(remaining_visits)`, `(negative_balance)`. |
| Ending soon report | Index on `membership_state_cache(effective_end_date)` plus active/canceled membership filter. |
| Low remaining report | Index on `membership_state_cache(remaining_visits)`. |
| Negative clients report | Index on `membership_state_cache(negative_balance)` where `negative_balance > 0`. |
| Inactive clients report | Index on `membership_state_cache(last_counted_visit_at)` or derived client summary if membership-level history is insufficient. |
| Daily visits report | Index on `visits(occurred_at)` plus active status; include `client_id` for drill-down. |
| Daily cash report | Index on `payments(occurred_at, status, method)` and optional covering index including `amount`. |
| Membership recalculation from visits | Index on `visit_consumptions(membership_id, status)` and `visits(id, occurred_at, status)`. |
| Freeze recalculation | Index on `freezes(membership_id, status, start_date, end_date)`. |
| Non-working period recalculation | Index on `non_working_period_applications(membership_id, status)` and `non_working_periods(start_date, end_date, status)`. |
| Audit timeline | Index on `business_audit_entries(entity_type, entity_id, recorded_at desc)` and `(account_id, recorded_at desc)`. |
| Changed after day close | Index on payments/visits/corrections by `occurred_at`, `recorded_at` and status. |

Use database constraints for invariants that must never be violated. Use domain/application validation for cross-table business rules that are too expressive for portable SQL, but keep tests around them.

## 6. Audit data model

Business audit answers: who performed a business action, under which account/session, on which entity, when the business event occurred, when it was recorded, what changed, and why.

Technical logs answer: request errors, latency, stack traces, auth failures, job status, backup/restore status and correlation IDs. They may reference `request_correlation_id`, but they are not the business record.

Audit entry required for successful commands:

- Client create/edit and card assignment/reassignment.
- MembershipType create/edit/deactivate.
- Issue/cancel/correct membership.
- Opening state/manual backfill creation.
- Visit record/cancel/correct/reallocate.
- Payment record/cancel/correct.
- Freeze add/cancel/correct.
- NonWorkingDay add/cancel/correct and affected membership confirmation.
- Negative closure workflow.
- Day reconciliation close/reopen if supported.
- Settings/permission-sensitive actions.

`business_audit_entries` should contain:

- `action_type`: stable enum-like string, for example `visit.recorded`, `payment.corrected`, `membership.recalculated`, `card.reassigned`.
- `entity_type` and `entity_id`: primary target.
- `related_entity_refs`: structured list/map for client, membership, payment, visit, correction IDs.
- `account_id`, `account_type`, `role`, `session_id`, `device_label`.
- `occurred_at`: business event time/date if the action creates or changes a business fact.
- `recorded_at`: system time of command success.
- `before_summary` and `after_summary`: owner-readable compact data, not raw technical diff only.
- `reason`/`comment`: required for corrections, cancellations, backfill/fallback, card reassignment and dangerous actions.
- `request_correlation_id`: bridge to technical logs.
- `entry_origin`: normal/manual_backfill/paper_fallback/future_import.

Append-only policy:

- Application workflows never update/delete audit rows.
- Corrections add new audit rows and correction source facts.
- If audit redaction is ever legally required, it must be a separate owner/operator procedure with its own record, not a normal business correction.

## 7. Reporting data access

Reports are query services over canonical records and Memberships read models. They do not own formulas.

Any selected business date maps to `[Europe/Kyiv midnight, next Europe/Kyiv
midnight)` and then to UTC instants. Predicates stay half-open; depending on DST,
the UTC interval is 23, 24 or 25 hours. Audit/history date filters and inactive
client dates use the same mapping. (ADR-017)

| Report | Source data | Drill-down | Notes |
|---|---|---|---|
| Daily cash/visits | `payments` where method cash, active status, `occurred_at` on business date; `visits` and active `visit_consumptions` where `occurred_at` on business date. | Payment rows, visit rows, cancellation/correction rows, related audit. | Excludes canceled payments/visits. If a corrected payment changes date/amount, old and new dates remain explainable. |
| Day close/reconciliation view | Daily report sources plus `day_reconciliations`. | Records with `recorded_at > closed_at` or correction rows after close. | Close records a reconciliation point; it does not freeze truth. |
| Memberships ending soon | `membership_state_cache.effective_end_date`, query date, active/canceled membership filter. | Issued membership, extension source facts, extension day explanation, audit. | `days_left <= 7`; `days_left` computed from query date. |
| Low remaining visits | `membership_state_cache.remaining_visits <= 2`. | Membership, counted visits/consumptions, cancellations, opening state if any. | Same state as client profile. |
| Negative clients | `membership_state_cache.negative_balance > 0`. | Membership, first negative visit, counted visits, negative closure facts if any. | Negative is membership state, not separate debt ledger in v1. |
| Inactive clients | `membership_state_cache.last_counted_visit_at` or derived client last counted visit summary. | Last counted visit, client profile, membership history. | Thresholds 14/30/60 days. Canceled visits do not count. Clients with no visits can be shown separately. |
| Client history | Canonical facts for client: memberships, visits, payments, freezes, non-working applications, corrections, audit summaries. | Exact source rows and audit rows. | History view should label backfilled/paper fallback entries. |

Optional read models:

- `daily_report_cache` can be introduced only after direct queries become slow. It must store `source_watermark`/`recalculated_at` and be rebuildable from payments/visits/corrections.
- Exported report snapshots are not source of truth in v1. If exported later, they are artifacts, not canonical totals.

Consistency rules:

- Client profile and reports must read the same membership state source.
- Daily totals must match drill-down rows.
- Audit is used for explanation and accountability, not for computing report totals.
- Corrections after day close change live totals and are labeled through report drill-down/audit.

## 8. Backfill/fallback model

Manual backfill and paper fallback are normal domain commands with extra metadata.

Manual backfill:

- Scope v1: active clients/memberships only, not full historical import.
- Use `entry_batches` with `batch_type = manual_backfill`.
- Create normal `clients`, `issued_memberships`, payments/visits/freezes if known.
- If full old history is incomplete, create `membership_opening_states` as explicit source fact with:
  - `opening_as_of_date`;
  - membership snapshot or linked type;
  - declared remaining visits or negative balance;
  - known effective end/extension state;
  - source reference and reason.
- Never generate fake historical visits just to make numbers match.

Paper fallback:

- During internet outage, business records visits/payments/freezes on paper.
- After recovery, staff enters records through normal commands with `entry_origin = paper_fallback`, actual `occurred_at`, current `recorded_at`, actor/session and reason/comment.
- Use `entry_batches` with `batch_type = paper_fallback` to group a paper sheet or outage period.
- Recalculation uses `occurred_at`; audit/report explanation uses both `occurred_at` and `recorded_at`.
- Historical daily reports may change after fallback entry. Drill-down must show entries recorded after the fact.

Future full import boundary:

- Do not write directly into production domain tables.
- Use `import_staging_records` or an external staging process.
- Validate duplicates, card uniqueness, phone normalization, membership snapshots, date ranges and opening states.
- Convert valid rows through domain commands so constraints, recalculation and audit stay consistent.

## 9. Migration and backup implications

Migration strategy:

1. Start with schema migrations for source facts, derived caches and audit. Keep seed data limited to roles/default owner setup if needed.
2. Add constraints early: FKs, checks, current card uniqueness, active opening state uniqueness and audit append policy.
3. Add derived tables with rebuild commands from day one. A migration should be able to rebuild `membership_state_cache` from source facts.
4. Treat enum-like values as controlled domains in code and DB checks where feasible, but leave migration path for adding values.
5. Use forward migrations for audit/source tables carefully. Dropping or rewriting source history should require explicit data migration plan and backup.
6. Add idempotency/duplicate-submit guards at command level, especially visits/payments from reception UI.
7. Before production use, run a restore rehearsal and then run domain consistency checks on the restored copy.
8. When a new recalculation contract invalidates derived caches, deploy code and
   run `scripts/rebuild-membership-state-caches.sh` before application traffic.
   For version `7`, every issued Membership must finish with current cache state.
   The command commits per Membership, reports processed counts and is rerun from
   canonical facts after interruption/non-zero exit; it creates no business audit.

Backup/restore implications:

- Provider-managed automated backups are required, with at least 30 days retention expectation and RPO no worse than 24 hours. Prefer PITR or several-hours RPO if hosting supports it.
- Restore must include all source tables, derived tables, audit and migration metadata. Technical logs may be restored separately depending on provider.
- Because derived tables are rebuildable, restore validation should compare rebuilt state with stored `membership_state_cache` and flag drift.
- Audit and source facts are high-value data. Backups should preserve transaction consistency between source fact, recalculation and audit rows.
- After restore to a point in time, paper fallback may be needed for business actions that happened after the restored point. Those entries must be recorded as `paper_fallback` or explicit recovery batch, not direct DB patches.
- Backup/restore job statuses belong to technical logs/operations records, not `business_audit_entries`, unless an owner-visible business action is performed in the app.

Suggested restore-check queries:

- Count active current card assignments and verify no duplicate card numbers.
- Rebuild all membership state and compare counts/remaining/effective dates with cache.
- Recalculate a sample daily cash report and compare payment drill-down totals.
- Verify audit rows exist for recent visits, payments, freezes, non-working day changes and corrections.
- Verify `entry_origin` markers exist for manual backfill/paper fallback batches.

## 10. Risks and validation scenarios

Main risks:

- Derived state drift if a command changes source facts without triggering Memberships recalculation.
- Report formulas accidentally duplicated outside Memberships.
- Card uniqueness violated during reassignment or concurrent client creation.
- Paper fallback/backfill entered as silent DB edits instead of audited commands.
- Audit overmodeled as technical logs or under-modeled as unreadable raw diffs.
- NonWorkingDay mass recalculation slow or partially applied without clear retry semantics.
- Negative closure workflow hiding old negative visits without explicit source facts.
- Date arithmetic mismatch with legacy paper convention for membership end date.
- UTC/Kyiv boundary mistakes around midnight or DST, including assuming every
  business day is 24 hours.
- Serving traffic with stale `membership_state_cache.recalculation_version`
  after a calculation-contract deploy.

Validation scenarios:

1. Issue membership from active MembershipType. Verify snapshot fields are copied and later MembershipType edit does not change issued membership.
2. Record visit when remaining visits is 1. Verify remaining becomes 0, daily visits includes the row, audit exists.
3. Query Kyiv spring/fall transition dates. Verify half-open UTC ranges contain
   exactly the local day (23/25 hours), boundary rows appear once and UTC-date
   mismatch does not move them to another business day.
4. Upgrade caches from recalculation version 6 to 7. Verify source/audit rows are
   unchanged, Kyiv-derived dates are repaired, all rows reach version 7 and a
   second rebuild reports verified state.
5. Record visit when remaining visits is 0. Verify remaining becomes -1, negative balance is 1 and first negative date is set.
6. Cancel the first negative visit. Verify visit remains visible, state recalculates, daily report excludes it and audit explains cancellation.
7. Add freeze 2026-01-10..2026-01-12. Verify inclusive 3-day extension and extension days explain the source.
8. Reject Freeze starting before Membership start or after locked pre-command effective end; verify no source fact, recalculation or success audit is committed.
9. Accept Freeze whose start is eligible and whose end crosses pre-command effective end; verify the full range participates in extension union.
10. Reject Freeze overlapping an active counted Membership Visit with `freeze_conflicts_with_visit`; verify Membership-first locking prevents a partial or stale write.
11. Add non-working period overlapping the freeze. Verify extension counts union calendar days, not sum of sources.
12. Cancel non-working period. Verify affected memberships recalculate and client history still shows add plus cancel.
13. Correct payment amount after day close. Verify live daily cash total changes, changed-after-close is visible and audit has before/after plus reason.
14. Search by exact card number. Verify exact current card match opens the correct client and duplicate current card assignment is blocked.
15. Search by last four phone digits. Verify non-unique matches produce list, not auto-open.
16. Enter active membership via manual backfill opening state. Verify no fake visits are generated, state is explainable and audit marks manual backfill.
17. Enter paper fallback visits/payments next day. Verify `occurred_at` drives reports for the business date while `recorded_at` and audit show late entry.
18. Close negative visits with a new membership. Verify `membership_negative_closures` and closure items list covered visits; old negative state is not silently hidden.
19. Restore database to staging. Rebuild membership state, compare caches, verify audit and source rows are transactionally consistent.

Open validation questions to settle before migrations:

- Whether NonWorkingDay applies only to overlapping active calendar days or full period once any overlap exists.
- Which denied permission attempts are business-audited versus technical-logged only.

Resolved before Visit migrations:

- ADR-005 accepts inclusive `start_date + duration_days - 1 day` arithmetic.
- ADR-014 allows multiple lifecycle-active Memberships and requires explicit `membership_id`; no automatic allocation is permitted.
- ADR-014 blocks membership Visit during an active Freeze covering the business date.
- ADR-014 uses explicit one_off/trial Visit kinds without consumption and permits a dedicated technical Client for unidentified visitors.

Resolved before Freeze migrations:

- ADR-015 defines lifecycle eligibility, start-date bounds, full-range storage,
  counted-Visit conflict handling and Membership-first lock order for AddFreeze.
