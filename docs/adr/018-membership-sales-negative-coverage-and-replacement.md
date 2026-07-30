# ADR-018: Membership sales, negative-visit coverage and issued-sale replacement

## Статус

Accepted - 2026-07-30.

## Контекст

Для звичайного продажу абонемента, закриття negative visits, виправлення
помилково виданого абонемента та paper fallback потрібні однакові межі:
гроші, source facts, Memberships recalculation, audit і reports не можуть
розходитись. Попередні ADR визначили snapshots, negative state, correction
history та fallback commands, але не фіксували exact sale-payment invariant,
one-off closure shape, replacement dependencies або paper-sheet metadata.

## Рішення

### Ordinary membership sale

- Active `ordinary` MembershipType має positive price. `IssueMembership` є
  одним ACID workflow: він створює issued Membership та один cash Payment рівно
  за issue-time price snapshot. Сума read-only і не вводиться працівником.
- Якщо створення Payment, recalculation або audit не вдається, transaction
  rollback не залишає ні Membership, ні Payment.
- Generic `CreatePayment` не створює і не доповнює ordinary sale. Standalone
  Payments лишаються тільки для окремо accepted non-membership contexts.
- `manual_backfill` opening state не є ordinary sale: неповну давню історію
  заводять окремим audited opening-state command без вигаданого Payment.
  Ordinary sale, внесений після збою з `paper_fallback`, усе одно створює
  exact-price Payment разом з Membership.
- Payment ordinary sale не можна окремо скасувати або змінити по сумі так, щоб
  active Membership лишився без exact-price Payment. Non-amount correction
  можлива лише коли сума все ще дорівнює snapshot price; для іншого випадку
  потрібен cancel/replacement sale workflow.

### Catalog and negative coverage

- MembershipType має explicit kind: `ordinary` або `one_off`; one-off types
  можуть бути множинними, мають `visits_limit = 1` і positive price. Owner
  create/edit/deactivate їх так само, як інші types. Kind не змінюється після
  створення; для іншого kind Owner деактивує старий type і створює новий.
  Історичні issued і closure records зберігають immutable snapshots
  name/duration/visits/price, тож catalog edit не змінює history.
- За negative visits Admin/Owner deliberate choice: лишити їх видимими,
  закрити one-off types або покрити новим ordinary Membership. Payment сам по
  собі нічого не закриває. Coverage завжди бере найдавніший ще непокритий
  negative Visit; exact Visits не обираються довільно.
- One-off closure вибирає active one-off type(s) і quantity, creates explicit
  closure item/line snapshots та один exact cash Payment за сумою lines.
  Items не перевищують open negative count; partial closure лишає видимий
  remainder; old Visits не переписуються й не видаляються.
- New-Membership coverage задає `start_date` як occurred business date
  найдавнішого covered Visit. Explicit allocation/coverage facts споживають
  visits limit: `1 <= coverage_count <= visits_limit_snapshot`. Якщо відкритих
  мінусових Visits більше за limit, remainder лишається visible; якщо менше,
  unused limit лишається positive. Coverage ніколи не створює новий Membership
  уже з negative remaining visits. Base/effective end рахуються від backdated
  start; Membership може одразу бути expired, що preview/warning показує без
  приховування. Issue, exact-price Payment і coverage facts створюються разом.
  Transaction locks source/covering Memberships і affected Visits,
  recalculates, audits and returns canonical rereads.

### Replacement and cancellation of issued sale

- `ReplaceIssuedMembership` доступний Admin (named або shared
  Reception/Admin) і Owner, reason required. Older/closed day не вимагає Owner
  approval; майбутня day-close policy лише позначає changed-after-close.
- В одній transaction old Membership стає corrected/replaced, його sale
  Payment canceled, створюються new issued Membership і new full exact-price
  Payment, зберігаються history, synchronous recalculation, audit і canonical
  rereads. Система не розраховує і не показує доплату, повернення або delta та
  не керує передачею грошей.
- `CancelIssuedMembershipSale` — окремий Admin/Owner cancel-only command з
  reason: old Membership і sale Payment canceled atomically, без hard delete,
  refund/delta calculation або переписування history.
- One-off closure payment не виправляється загальним `CorrectPayment` окремо від
  closure. Mistaken closure скасовується або замінюється reason-required
  coverage-correction command, який разом змінює closure items, active Payment,
  recalculation, report і audit; система так само не рахує cash delta/refund.
- Replacement preview перелічує counted Visits, Freezes, NonWorkingDay
  applications і negative-closure links. Нічого не переноситься silently.
  Command succeeds лише якщо active dependencies валідно представлені explicit
  replacement/reallocation facts за чинними rules; інакше full rollback з exact
  blockers, які Admin має виправити або скасувати окремим workflow.

### UI, audit, reports and fallback

- UI показує всі available methods, dates, quantities and consequences, але не
  recommends/preselects method чи type. Inactive/stale type reject before
  writes. Commands remain idempotent and use locking/concurrency checks.
- Daily report reads canonical active Payment rows; canceled original and
  replacement explanation stay visible in drill-down/history/audit.
- After outage Admin/Owner creates one `entry_batch` per numbered paper sheet
  and enters sheet number once. An outage may have several sheet batches.
  Every row has stable line number (system-assigned allowed), actual
  `occurred_at`, server `recorded_at`, actor/session, required explanation and
  `entry_origin=paper_fallback`. Sheet and line are first-class batch/row
  metadata, not free text. Entry uses normal commands, then reconciliation
  through daily report/audit/history.
  Paper ordinary sale follows the same exact full-payment invariant.

## Наслідки

- Ordinary sales become explainable exact-price pairs rather than optional
  membership/payment records.
- Negative state remains visible until explicit oldest-first coverage or
  closure facts explain it.
- Sale errors retain cancellation/replacement history without app-level cash
  settlement logic.
- Day close/reconciliation policy remains a separate unresolved decision; this
  ADR only requires changed-after-close labeling when such a policy exists.

## Що це означає для реалізації

- Add source schema for MembershipType kind, sale links/invariants, negative
  closure lines/snapshots/payments, oldest-first coverage allocations,
  replacement/cancellation facts and paper batch sheet/line metadata.
- Implement commands, deterministic locks, authorization, idempotency,
  recalculation, audit and canonical rereads atomically; preserve existing
  source facts rather than direct mutations.
- Add PostgreSQL, domain, command, report/audit and tablet/phone UI tests for
  omitted/under/over payment rejection; snapshot immutability; partial/full
  one-off closure; oldest-first; backdated/expired coverage; replacement/cancel
  rollback, dependencies, idempotency, concurrency and reports; and paper
  sheet/line reconciliation.

ADR-018 supplements ADR-005, ADR-010, ADR-011, ADR-012 and ADR-014. It
supersedes their deferred or optional wording only where this decision states a
more specific contract.
