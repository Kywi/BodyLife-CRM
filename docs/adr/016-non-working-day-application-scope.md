# ADR-016: NonWorkingDay application scope and contribution policy

## Статус

Accepted - 2026-07-16.

## Контекст

Milestone 8 не може безпечно реалізувати `PreviewNonWorkingDayImpact`,
`AddNonWorkingDay` або `CorrectNonWorkingDay`, доки не визначено:

- який lifecycle/date state робить issued Membership affected;
- чи додається лише date intersection з Membership, чи вся тривалість
  закриття залу;
- який canonical state використовується для eligibility, щоб period не робив
  Membership eligible власним extension;
- чи є confirmed application scope незмінним snapshot або набором, який
  пізніше тихо розширюється;
- як preview захищає Owner від stale mass action;
- які Memberships перераховуються після replace/cancel correction.

Початкова product requirement каже, що всім affected active Memberships
додаються дні, кількість яких дорівнює тривалості закриття залу. Водночас
ADR-005 вимагає source facts та union calendar dates, ADR-012 робить workflow
Owner-only, а ADR-015 уже встановлює locked pre-command Membership state для
індивідуального Freeze.

Якщо implementation самостійно обере intersection-only policy, Membership,
який закінчується всередині закриття, може бути продовжений лише до останнього
закритого дня і фактично не отримати компенсованих open days. Якщо scope
переобчислюється неявно після підтвердження, Owner більше не може пояснити,
кого саме він підтвердив.

## Варіанти

- Додавати тільки календарні дати intersection між period і pre-command
  Membership effective interval.
- Якщо є будь-який inclusive overlap, додавати весь inclusive period.
- Вважати eligible тільки Membership, active на першій даті period, і додавати
  весь period.
- Зберігати confirmed scope snapshot або автоматично додавати/видаляти
  applications після майбутніх Membership/Freeze corrections.
- Довіряти preview як source of truth або завжди revalidate scope у command
  transaction.

## Рішення

### Eligibility snapshot

`PreviewNonWorkingDayImpact` і command оцінюють issued Memberships за одним
pure policy:

- lifecycle status має бути `active`; `canceled` і `corrected` Memberships не
  входять у новий scope;
- Client operational status сам по собі не змінює entitlement;
- period і Membership canonical date interval мають inclusive overlap:
  `period_start <= pre_command_effective_end_date` і
  `period_end >= membership_start_date`;
- current server date не є eligibility boundary: backdated, current і future
  periods використовують те саме date rule;
- pre-command canonical state включає всі вже accepted active source facts,
  але виключає proposed period; для `replace_range` він також виключає старий
  period, який замінюється.

Отже, date-expired сьогодні Membership може бути affected backdated period,
якщо він був date-active в цьому interval. Future-start Membership може бути
affected future period. Membership, який не має жодного overlap до proposed
source, не стає eligible через extension цього ж source.

### Full-period contribution

Після будь-якого inclusive overlap application contributes **весь** inclusive
NonWorkingDay range:

```text
applied_start_date = period_start_date
applied_end_date   = period_end_date
application_days  = period_end_date - period_start_date + 1
```

Range не clipping ні до `membership_start_date`, ні до locked pre-command
`effective_end_date`. Це свідомий product rule: confirmed affected Membership
отримує компенсацію, рівну повній тривалості закриття залу. Тому Membership,
який починається або закінчується всередині period, теж отримує весь period.
Owner бачить applied range і estimated before/after state у preview до
підтвердження.

Boundary examples for a period `2026-01-30..2026-02-02`:

- Membership `2026-01-01..2026-01-31` overlaps and receives all 4 dates;
- Membership `2026-02-01..2026-02-28` overlaps and also receives all 4 dates;
- Membership starting `2026-02-03` has no overlap and receives 0 dates.

Memberships лишається єдиним calculator. Воно materializes active application
dates і рахує union unique calendar dates разом із active Freezes та supported
adjustments. Overlap не дає double extension. Canceled/corrected period або
application contributes zero active days, але source history не видаляється.

### Confirmed application scope

Scope є immutable snapshot успішної Owner command transaction, а не
автоматично рухомим набором:

1. Preview повертає exact ordered set Membership IDs, Client IDs, applied
   ranges, overlap warnings, estimated changes, expiry та tamper-resistant
   confirmation token/fingerprint.
2. `AddNonWorkingDay` або range-changing correction відкриває consistent
   PostgreSQL transaction snapshot, повторно обчислює policy scope і locks
   affected Membership rows у deterministic order.
3. Token має збігатися з exact Membership/application set and ranges. Expired
   token повертає `preview_expired`; будь-яка різниця повертає
   `affected_scope_changed` без partial source/audit/cache writes.
4. Successful command stores one `non_working_period_applications` row per
   confirmed Membership with the full period as applied range.

Membership, issued after this transaction snapshot, або Membership, який
пізніше став eligible через іншу source correction, не додається до вже
confirmed scope мовчки. Так само пізніша зміна Membership state не видаляє
confirmed application. Для retroactive change потрібна нова explicit
Owner-reviewed correction/adjustment workflow з audit; direct database patch
заборонений. Це зберігає точне значення Owner confirmation у v1.

### Correction and recalculation

- `replace_reason` preserves the exact confirmed application set/ranges; reason
  history changes through retained correction/replacement facts.
- `replace_range` previews and computes a new snapshot from canonical state with
  the old period excluded. It retains old period/applications as corrected and
  creates the replacement period plus its confirmed applications.
- `cancel` retains the period and applications as canceled and creates an
  explicit cancellation fact; no replacement scope exists.
- The transaction recalculates the union of Membership IDs in the old active
  scope and the new active scope, then appends one Owner-readable audit event.
- Source, applications, recalculation, audit and idempotency outcome commit or
  roll back together. UI rereads canonical state after success.

## Відхилено для v1

- intersection-only contribution;
- eligibility based only on current server date or Client active marker;
- self-expanding eligibility that includes the proposed period in its own
  pre-command effective end;
- silent clipping of the confirmed period per Membership;
- silently adding/removing applications after Owner confirmation;
- trusting a stale/expired preview;
- partial-success mass recalculation reported as complete;
- direct edit of `effective_end_date` or application rows as correction.

## Наслідки

- Owner can explain the exact confirmed Membership set and full closure range.
- A Membership that overlaps only one period endpoint still receives the full
  closure duration; this is deliberate and must be visible in preview.
- Snapshot semantics keep later Membership issuance/corrections from silently
  rewriting a previous mass action; exceptional retroactive entitlement needs
  an explicit audited action.
- Correction recalculation has a finite old/new scope and no recursive
  fixed-point search.
- Milestone 8 can now define schema, preview and commands without inventing
  application semantics in EF, SQL or Razor code.

## Що це означає для реалізації

- Add a Memberships-owned pure NonWorkingDay eligibility/contribution contract
  and edge-case tests before persistence.
- Persist the exact applied range and one active application per period/version
  and Membership; keep corrected/canceled rows explainable.
- Preview tokens bind period input, ordered scope IDs/ranges and expiry; commands
  revalidate from canonical state in a consistent transaction snapshot.
- PostgreSQL tests cover both inclusive endpoints, no overlap, lifecycle
  exclusion, current/future/backdated cases, full-period boundary behavior,
  stale preview, deterministic locking, rollback and old/new correction scope.
- Domain tests cover union with overlapping Freezes/NonWorkingDays and inactive
  source rows. Playwright later covers Owner-only preview/confirm/correct,
  full-period boundary explanation, busy submit and canonical reread.
