# ADR-015: Freeze range eligibility policy

## Статус

Accepted - 2026-07-16.

## Контекст

Milestone 8 не може безпечно реалізувати `AddFreeze`, доки не визначено:

- чи можна додати Freeze до canceled/corrected Membership;
- чи може Freeze починатися до `start_date` або після canonical
  `effective_end_date` Membership;
- чи може range, що почався під час чинності Membership, завершуватися після
  його поточної effective end date;
- що робити з backdated Freeze, який перекриває вже counted Membership Visit;
- який стан і які рядки треба lock перед перевіркою.

Якщо handler сам обере правило, одна й та сама заморозка може бути прийнята в
одному workflow та відхилена в іншому. Особливо небезпечно дозволити active
counted Visit і active Freeze на ту саму calendar date: це одночасно спише
заняття та продовжить Membership, що суперечить ADR-014.

## Варіанти

- Дозволити будь-який inclusive range для будь-якого issued Membership.
- Вимагати, щоб увесь range був усередині pre-command effective date interval.
- Вимагати, щоб Freeze почався всередині pre-command effective date interval, але дозволити
  йому завершитися пізніше.
- Обрізати range до Membership dates або мовчки ігнорувати дні поза ними.
- Дозволити backdated overlap з counted Visits, дозволити з warning або
  відхилити до explicit Visit correction.

## Рішення

### Membership eligibility

`AddFreeze` приймає тільки issued Membership із lifecycle status `active`.
`canceled` або `corrected` source не можна продовжувати новою Freeze. Client і
Membership мають існувати та відповідати composite ownership relationship.

Range eligibility перевіряється за canonical Memberships state безпосередньо
перед створенням source fact:

- `start_date <= end_date`, обидві дати inclusive;
- Freeze `start_date` не може бути раніше issued Membership `start_date`;
- Freeze `start_date` не може бути пізніше pre-command canonical
  `effective_end_date`;
- Freeze `end_date` може бути пізніше pre-command `effective_end_date`, якщо
  Freeze почався в допустимому effective date interval;
- увесь прийнятий inclusive range є source range, його не обрізають і не
  переписують мовчки.

Отже, Freeze може бути backdated або запланованим на майбутню дату всередині
pre-command effective date interval. Поточна server date сама по собі не
дискваліфікує backdated source. Повністю post-expiry range не може зробити вже
expired Membership активним заднім числом. Якщо попередня accepted Freeze
спочатку продовжила effective end date, наступний Freeze перевіряється вже
проти нового canonical state.

### Visit conflict

Active counted Membership Visit не може inclusive потрапляти в новий active
Freeze range. `AddFreeze` fail-closed повертає stable
`freeze_conflicts_with_visit` і не створює Freeze, derived rows, idempotency
success або business audit success entry.

Actor має спочатку cancel/correct помилковий Visit або змінити Freeze range.
One-off/trial Visits не є Membership consumption і не блокують Freeze.
Canceled Visit/consumption також не блокує range.

Це правило симетричне ADR-014: `MarkVisit` не може спожити frozen Membership, а
`AddFreeze` не може накрити вже спожитий counted Membership day.

### Range overlap and contribution

Overlap між кількома Freezes та майбутніми applicable NonWorkingDay sources
дозволений. Memberships рахує union unique active calendar dates; overlap не
створює double extension. Canceled source лишається в history/explanation, але
contributes zero active days.

ADR не встановлює довільний business maximum duration. Range та derived
`effective_end_date` мають бути representable у підтримуваному calendar type;
overflow або неможливий canonical calculation fail the command. Якщо production
evidence вимагатиме окремого duration cap, це буде нове product decision, а не
прихована UI чи handler constant.

### Transaction and locking

`AddFreeze` виконує в одній PostgreSQL transaction:

1. authorizes the Owner/Admin actor and active session, then checks idempotency;
2. locks selected issued Membership row;
3. rebuilds/reads pre-command canonical Membership state;
4. checks lifecycle, Client ownership, range eligibility and overlapping active
   counted Membership Visits;
5. inserts the Freeze source fact;
6. synchronously rebuilds Membership extension union, state cache and
   explanation rows;
7. appends `freeze.added` audit with range, inclusive days, reason and
   before/after effective-end summary;
8. stores idempotency success and returns Client/Profile canonical reread target.

`MarkVisit`, `AddFreeze` and future `CancelFreeze` use the same order: lock the
Membership before reading or changing relevant Freeze/Visit source rows. A
failure in source insert, recalculation, audit or idempotency rolls back the
whole action.

## Відхилено для v1

- Freeze for canceled/corrected Membership;
- range that starts before Membership start or after pre-command effective end;
- silent clipping to issued/effective dates;
- active counted Visit plus active Freeze on the same Membership calendar date;
- warning override for a Visit conflict;
- direct edit of `effective_end_date`;
- UI-only eligibility or overlap calculations.

## Наслідки

- Reception receives a deterministic range rule before the command/UI exists.
- A Freeze that starts while Membership is active can honestly cross its former
  end date and extend by the complete inclusive range.
- Backdated entry stays possible, but cannot create contradictory Visit and
  Freeze facts.
- Memberships remains the only owner of effective end and extension union.
- Milestone 8 can implement `AddFreeze` without inventing range behavior in
  Infrastructure or Razor code.

## Що це означає для реалізації

- Add a Memberships-owned pure Freeze eligibility result/error contract and
  focused tests before relying on Infrastructure validation.
- Rebuild canonical Membership state before validating pre-command effective
  end under the Membership lock.
- Query active counted Membership Visits for inclusive overlap under the same
  transaction and return `freeze_conflicts_with_visit` without partial writes.
- PostgreSQL tests must cover both inclusive endpoints, before-start,
  post-expiry, end-after-effective acceptance, overlap union, active/canceled
  Visit conflict, backdated entry, idempotency, lock serialization and rollback.
- Playwright tests later cover tablet/phone range errors, busy submit and
  canonical profile reread; UI never predicts the effective end date itself.
