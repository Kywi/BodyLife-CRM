# ADR-017: Business time zone and UI localization

## Статус

Accepted - 2026-07-22.

## Контекст

BodyLife CRM зберігає `occurred_at`, `recorded_at`, audit timestamps і технічні
timestamps як точні моменти часу. PostgreSQL `timestamptz`, application commands
і technical logs можуть безпечно працювати в UTC, але reception UI не повинен
показувати UTC як час залу.

Без одного явного правила різні екрани можуть:

- форматувати один instant у різних time zones;
- трактувати вибрану report date як UTC calendar day замість дня залу;
- записувати `datetime-local` як UTC wall time;
- по-різному обробляти DST gap або двозначний fall-back час;
- виводити англомовні/ISO timestamps усередині локалізованого UI;
- залишити старий `membership_state_cache`, у якому date-derived values були
  обчислені за UTC date.

Для одного залу v1 не потрібна tenant-specific або user-selectable time zone.
Потрібні фіксований business calendar, одна conversion boundary та керований
перерахунок derived state.

## Варіанти

- Показувати UTC всюди, включно з reception UI.
- Використовувати time zone операційної системи або браузера.
- Зберігати локальний wall time без offset як canonical timestamp.
- Фіксувати business time zone залу, зберігати instants у UTC і локалізувати
  тільки на command/query/UI boundaries.
- Під час DST overlap обирати перший або другий можливий instant або вимагати
  окремий offset від користувача.

## Рішення

### Canonical instant і business calendar

- Canonical instants у commands, PostgreSQL `timestamptz`, business audit і
  technical logs нормалізуються та зберігаються в UTC.
- Фіксована business/UI time zone залу - IANA `Europe/Kyiv`.
- `FLE Standard Time` є лише Windows platform fallback для того самого
  календаря; це не окрема product setting.
- Time zone сервера, контейнера, браузера або пристрою не визначає business
  date і не змінює результат query.
- `DateOnly` business values лишаються календарними датами без offset. Коли
  дата походить від instant, вона визначається після conversion у
  `Europe/Kyiv`.

### UI input і output

- Усі видимі timestamps форматуються після conversion у `Europe/Kyiv` через
  active UI culture. Для поточного v1 це `uk-UA` за замовчуванням і підтриманий
  `en-US` alternative.
- Звичайний UI не додає `UTC`, `Europe/Kyiv`, `EET/EEST` або numeric offset до
  локалізованого timestamp. Контекст застосунку вже фіксує time zone залу.
- HTML `datetime-local` означає Kyiv wall time, а не UTC. Після server-side
  validation значення конвертується в canonical UTC instant до command.
- UI, Razor templates і JavaScript не володіють conversion formula. Вони
  використовують спільний server-side business-time contract.

### DST contract

- Local wall time, якого не існує під час spring-forward gap, повертає
  `validation_failed`; source fact, audit та idempotency success не
  створюються.
- Для ambiguous fall-back wall time v1 детерміновано обирає перший
  chronological occurrence: більший із двох valid UTC offsets, який дає
  раніший UTC instant.
- Command boundaries відхиляють default/min/max або інші instants і dates, які
  не можна безпечно представити в business calendar, до відкриття transaction.

### Business day і date filters

Вибрана business date перетворюється у half-open UTC instant range:

```text
from_inclusive = Europe/Kyiv local midnight at business_date
to_exclusive   = Europe/Kyiv local midnight at business_date + 1 day
```

Обидві межі конвертуються окремо. Тому interval має 23, 24 або 25 годин, але
завжди охоплює рівно один локальний календарний день залу. Daily Visits,
Payments, cash reconciliation, inactive-client dates, Client History і Audit
date filters використовують цей contract, а не UTC midnight arithmetic.

### Membership derived state version 7

Kyiv-derived dates змінюють recalculation contract, тому
`membership_state_cache.recalculation_version` підвищується до `7`.

- Source facts не переписуються і schema migration не вигадує історію.
- Перед подачею application traffic після deploy усі issued Membership caches
  обов'язково перебудовуються canonical Memberships rebuilder-ом через
  `scripts/rebuild-membership-state-caches.sh`.
- Bulk rebuild детерміновано обходить Membership IDs, комітить один Membership
  за раз і може бути безпечно повторений. Після interruption або non-zero exit
  operator усуває причину і запускає ту саму command ще раз.
- Rebuild змінює тільки відновлюваний derived state, тому не створює business
  audit. Його start/result/failure і processed counts належать technical logs.
- Звичайні business commands, включно з NonWorkingDay mass action, зберігають
  свої наявні atomic transaction boundaries; operational bulk rebuild не є
  дозволом на partial business workflow.

## Відхилено для v1

- UTC timestamps як reception-facing presentation;
- browser/device/server-local time як неявна business time zone;
- user-selectable або per-account time zone;
- local wall timestamps без canonical UTC instant;
- UTC-midnight filters для Kyiv business dates;
- silent normalization DST gaps;
- недетермінований вибір occurrence під час DST overlap;
- пряме редагування cache rows або source facts для переходу на version 7;
- treating a partial cache rebuild as completed release readiness.

## Наслідки

- Один instant може мати інше visible calendar date, ніж його UTC date; це
  очікувана поведінка.
- Report interval length більше не завжди дорівнює 24 годинам.
- Backdated local input має стабільну DST policy і однаково працює на Linux та
  Windows.
- UI snapshots залежать від active culture, але business selection і persisted
  instant не залежать від мови.
- Deploy із cache contract version 7 має окремий mandatory pre-traffic
  operational gate; rerun є recovery path після partial progress.

## Що це означає для реалізації

- Shared Kernel надає єдиний `BusinessTimeZone` contract; модулі не дублюють
  IANA IDs, offset arithmetic або DST rules.
- Commands validate and normalize business instants before transactions.
- Queries derive business dates and UTC ranges only through the shared
  contract; reports keep half-open predicates.
- Web presentation centralizes culture-aware local formatting і parsing of
  `datetime-local`.
- Tests cover winter/summer conversion, spring gap, fall overlap, 23/24/25-hour
  days, UTC-midnight/Kyiv-date differences, min/max rejection, no-residue
  validation failures, localized UI і idempotent cache rebuild from version 6.
- Release evidence records successful version-7 bulk rebuild before traffic.

ADR-017 supplements ADR-003, ADR-005, ADR-006, ADR-007, ADR-009, ADR-010 and
ADR-012. It supersedes none of them.
