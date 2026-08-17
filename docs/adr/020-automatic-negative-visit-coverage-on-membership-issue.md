# ADR-020: Automatic negative-visit coverage on ordinary Membership issue

## Статус

Accepted - 2026-08-17.

## Контекст

ADR-018 ввів пояснювані oldest-first facts для покриття від’ємних Visits новим
ordinary Membership, але переклав на Reception технічний вибір способу та
кількості. У формі `IssueMembership` це дало три варіанти, серед яких був
недоступний, і окреме ручне поле кількості. Для оператора це виглядає як
незавершений підпроцес і дозволяє видати абонемент, свідомо лишивши конкретні
від’ємні Visits непокритими.

Product Owner уточнив очікуваний workflow: видача нового ordinary Membership
має сама використати його доступний visits limit для найдавніших конкретних
від’ємних Visits. Reception перевіряє наслідок у preview, але не визначає
алгоритм і не вводить кількість.

## Рішення

### Automatic oldest-first allocation

- `IssueMembership` не приймає від UI negative-handling method або coverage
  quantity. За наявності конкретних непокритих від’ємних Visits сервер
  автоматично обчислює
  `coverage_count = min(open_concrete_negative_visits, visits_limit_snapshot)`.
- Exact Visits беруться тільки з канонічного ordered set Memberships за
  `occurred_at`, effective consumption `recorded_at`, Visit id і source
  Membership id. Reception не може вибрати інші Visits, змінити порядок або
  зменшити кількість.
- Якщо `coverage_count > 0`, `start_date` нового Membership примусово дорівнює
  Kyiv business date найдавнішого covered Visit. Coverage facts споживають
  visits limit; new Membership не починає з від’ємного remaining balance.
- Якщо конкретних Visits більше за limit, автоматично покривається найдавніша
  limit-sized частина, а concrete remainder лишається видимим у canonical
  profile/report. Якщо Visits менше, невикористана місткість нового Membership
  лишається positive.
- Active ordinary type з `visits_limit = 0` не може бути виданий, коли існує
  хоча б один concrete negative Visit: preview пояснює, що треба обрати тип із
  місткістю або використати окремий one-off closure. Якщо concrete Visits
  відсутні, zero-limit type підпорядковується звичайним правилам issue.

### Unknown opening/backfill balance

- `membership_opening_states` або інший old/backfill negative remainder без
  конкретного Visit id не перетворюється на synthetic Visit і не входить до
  automatic allocation.
- За unknown-only state ordinary Membership можна видати зі звичайною датою;
  unknown balance лишається видимим з чесним warning. За mixed state
  автоматично покриваються тільки concrete Visits, а unknown remainder
  зберігається.

### Preview, concurrency and transaction

- Server preview є read-only: показує автоматичну кількість, forced start,
  resulting remaining/effective end, concrete та unknown remainder і
  already-expired warning. Він не показує `LeaveVisible`, ручну quantity або
  disabled explicit-closure option.
- Preview повертає opaque signed coverage-set token, що зв’язує Client,
  MembershipType version, proposed start, total/unknown negative balance і
  deterministic ordered candidate set. Submit спочатку lock-ить Client,
  source Memberships, Visits та consumptions, повторно обчислює allocation і
  відхиляє expired/mismatched token як `stale_state`.
- `IssueMembership`, exact-price membership-sale Payment, explicit
  new-membership closure/allocation facts, recalculation, audit та idempotency
  commit-яться в одній transaction. Automatic allocation не створює окремий
  closure Payment.
- Audit/history зберігають policy name, locked ordered covered Visit ids/count,
  source і covering Membership ids, forced start, pre/post remainder та sale
  snapshots. Історичні `leave_visible`/manual-decision audit values лишаються
  читабельними, але новий Issue workflow їх не створює.

### Corrections and workflow boundaries

- One-off negative closure лишається окремою явною дією з exact Payment,
  preview, confirmation, correction/cancellation та oldest-first facts.
- Active automatic coverage dependency, як і попереднє new-membership
  coverage, блокує silent sale cancel/replacement. Спочатку Admin/Owner
  reason-required correction workflow скасовує або замінює coverage facts,
  після чого sale можна виправити за ADR-018.
- Поточні one-off/trial sales ADR-019 не покривають старі negative Visits і цим
  рішенням не змінюються.
- Це рішення не визначає окремо кількість lifecycle-active Memberships і не
  змінює відповідний contract.

## Наслідки

- Reception більше не бачить технічне підменю й не може випадково пропустити
  конкретний мінус або покрити не ту кількість.
- Memberships лишається єдиним власником allocation formula й canonical state;
  UI лише показує server preview.
- Backdated Membership може одразу бути expired, а partial/unknown remainder
  може лишитися negative; обидва наслідки мають бути видимими до submit і після
  canonical reread.
- Signed preview token і повторний locked calculation додають contract/test
  роботу, але не дозволяють stale preview непомітно змінити top-K allocation.

## Що це означає для реалізації

- Прибрати negative decision/count із поточного IssueMembership UI/query/input
  contract; зберегти historical enum/audit display лише для старих facts.
- Додати automatic allocation policy та signed preview-token validation;
  command отримує тільки preview token, а кількість і Visit ids визначає сервер.
- Оновити domain, PostgreSQL, command, audit/report/history та tablet/phone UI
  тести для no-negative, 1/K/>K concrete, zero-limit, unknown-only, mixed,
  expired, stale token, retry/idempotency, concurrency, correction dependency і
  exact single membership-sale Payment.

ADR-020 supplements ADR-005, ADR-010, ADR-014, ADR-017, ADR-018 and ADR-019. It
supersedes ADR-018's deliberate `leave_visible`/new-Membership method and manual
coverage-count choice only for current ordinary `IssueMembership`, and
supersedes ADR-019's no-automatic-quantity wording only for that same path.
