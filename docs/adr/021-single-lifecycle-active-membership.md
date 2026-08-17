# ADR-021: Single lifecycle-active issued Membership per Client

## Статус

Accepted - 2026-08-17.

## Контекст

ADR-014 дозволив кілька lifecycle-active issued Memberships, щоб overlap і
negative history не зникали автоматично. Поточний `IssueMembership` тому завжди
створює ще один `active` row і не змінює попередній. Навіть Membership із
remaining visits `0` лишається lifecycle-active, а профіль переходить у
`ambiguous` state після наступної видачі.

Product Owner уточнив інваріант: у Client має бути щонайбільше один
lifecycle-active issued Membership. Це не скасовує два прийняті способи
покриття concrete negative Visit debt: ADR-020 автоматично використовує новий
ordinary Membership, а ADR-018 лишає окреме one-off closure. Lifecycle старого
Membership і видимість непокритого concrete або unknown negative state мають
бути різними поняттями.

## Рішення

### Cardinality і explainable closure

- У committed state Client має `0..1` issued Membership зі `status = active`.
  PostgreSQL забезпечує це partial unique index за `client_id` для `active`
  rows; command validation і locks дають зрозумілу помилку до або замість raw
  constraint failure.
- Додається non-correction lifecycle status `closed`. Він означає, що
  Membership більше не можна вибирати для нового Visit, Freeze або іншої
  active-Membership дії. `closed` не означає cancel/correct sale, refund або
  скасування пов'язаного exact-price Payment.
- Кожен перехід у `closed` має append-only closure source fact із source
  Membership, optional successor Membership, canonical reason, actor/session,
  correlation/idempotency context, entry origin, occurred/recorded time і
  поясненням, де воно потрібне. Status є current projection цього факту, а не
  єдиною історією переходу.
- Closure facts мають `UNIQUE(source_membership_id)`, забороняють
  `source_membership_id = successor_membership_id` і через same-client composite
  FKs/locked validation гарантують, що optional successor належить тому самому
  Client. Deferred commit validation узгоджує `closed` status рівно з одним
  closure fact, а `active` - з відсутністю такого факту.
- Ordinary completion/rollover не використовує `canceled` або `corrected`:
  ці statuses лишаються тільки для ADR-018 sale cancellation/replacement.

### `IssueMembership` transition matrix

| Locked pre-command state | Результат ordinary issue |
|---|---|
| Немає lifecycle-active Membership | Створити один new active Membership. Historical concrete negative Visits, якщо є, обробляються ADR-020; unknown opening remainder лишається видимим warning. |
| Active Membership має remaining `0` | У тій самій transaction закрити його як zero-balance predecessor і створити new active Membership. |
| Active Membership має concrete, unknown або mixed negative balance | Спочатку застосувати locked ADR-020 oldest-first allocation до concrete Visits, потім закрити predecessor і створити new active Membership. Concrete remainder лишається visible і coverable; unknown remainder лишається visible, але не синтезується й не покривається цими двома Visit-based способами. |
| Active Membership має positive remaining visits | Звичайну видачу відхилити без записів: невикористані visits не можна мовчки втратити. Помилковий sale виправляється чинним ADR-018 replace/cancel workflow; окреме deliberate forfeiture/early-close рішення не додається цим ADR. |
| Active Membership є expired-by-date або future-start | Застосувати ту саму signed-balance policy, що вище. Дата сама по собі не обходить lifecycle cardinality і не дозволяє мовчки втратити positive visits. |
| Backdated або `paper_fallback` issue | Застосувати ті самі locks, transition rules, source facts і audit; historical date overlap дозволений, але після commit active row одна. |

- Preview показує predecessor, closure consequence, ADR-020 allocation і
  residual debt. Signed preview material додатково зв'язує id/status/version
  поточного active Membership, щоб submit не закрив інший або вже змінений row.
- `IssueMembership`, full one-off closure, coverage correction і dependent Visit
  correction використовують один lock hierarchy: Client; усі affected
  Memberships за stable id; opening/Visit/consumption rows у canonical ADR-020
  order; closure/allocation rows; Payment dependencies. Predecessor closure, new
  Membership, exact sale Payment, allocation facts, recalculation, audit та
  idempotency commit-яться разом.
- Correction/cancellation нового sale не реактивує predecessor автоматично.
  Active closure/allocation dependencies мають бути показані й виправлені
  explicit reason-required workflow або команда відхиляється без partial state.
- Будь-яка Visit/coverage correction, що змінила б `closed` Membership із
  non-positive balance на positive, відхиляється як exact lifecycle dependency
  до записів. Automatic reactivation, silent stranded credit і перенесення
  visits у current Membership заборонені; окремий reason-required lifecycle
  correction/forfeiture workflow потребує нового product decision.

### One-off closure і negative history

- One-off negative closure лишається окремим ADR-018 aggregate з deliberate
  method/quantity, immutable line snapshots і одним exact Payment. Воно не
  створює successor Membership.
- Partial one-off closure current negative Membership лишає його єдиним active
  Membership. Full closure, що доводить його locked canonical balance до `0`,
  закриває його в тій самій transaction. Closure історичного debt вже
  `closed` Membership не змінює current active Membership.
- Open concrete negative Visits можуть належати `closed` historical Membership
  і лишаються eligible для ADR-020 та one-off oldest-first coverage. Memberships
  public queries, candidate selection, Negative Clients report і resolver не
  мають фільтрувати їх лише через lifecycle status.
- Unknown opening/backfill remainder на active або `closed` Membership не має
  Visit ids, тому ADR-020 і one-off closure його не споживають. Він лишається
  visible pending a separate audited opening-state reconciliation decision; цей
  ADR не вигадує synthetic Visits, Payment або coverage fact.
- `closed` Membership не приймає нових Visits і не реактивується від coverage
  correction. Дозволена correction змінює source facts і recalculated
  historical non-positive balance; positive sign crossing блокується правилом
  вище, а lifecycle transition лишається окремою explainable history.

### Queries, UI, audit і reports

- Current Membership query повертає тільки `none` або `single`; `ambiguous` не є
  допустимим committed product state після цього corrective slice.
- Client profile показує один current Membership окремо від historical
  Memberships і aggregate open debt. `closed` history називає reason та
  successor, якщо він є; sale Payment зберігає свій фактичний status.
- `membership.issued` audit пояснює atomic predecessor closure разом із новим
  sale та ADR-020 allocation. Full one-off audit так само пояснює lifecycle
  closure; окрема UI-дія не вигадується для автоматичного transition.
- Reports читають canonical source facts і Memberships-owned public state.
  Ending/low/Freeze/Visit choices використовують тільки active Membership;
  Negative Clients і history включають непокритий debt із active та closed
  Memberships без дублювання формул.

### Greenfield baseline

- Система ще не має deployed production database. Новий status, closure source
  fact, FKs/checks/indexes і partial unique invariant додаються до єдиного
  `InitialBaseline`; локальні та test databases перевідтворюються з нуля.
- Не додається migration chain або runtime repair для довільних legacy rows із
  кількома active Memberships. Якщо production data з'явиться до реалізації,
  потрібен окремий data-classification/migration plan до застосування index.

## Наслідки

- Reception завжди бачить один operational current Membership. Concrete
  negative history не приховується й далі закривається new Membership або
  one-off; unknown opening remainder лишається visible, а не fake-coverable.
- Zero-balance rollover стає автоматичним; positive balance не forfeited
  silently.
- Negative queries більше не можуть прирівнювати `status = active` до наявності
  debt, тому corrective slice торкається persistence, commands, reports, audit,
  profile UI і PostgreSQL tests до початку Milestone 10.6.
- Додається lifecycle source fact та concurrency work, зате cardinality
  захищається базою, а не лише UI convention.

## Що це означає для реалізації

1. Узгодити architecture/domain/data/interaction/UI/operations/quality
   contracts і прибрати multiple-active assumptions ADR-014.
2. Розширити sole `InitialBaseline`, lifecycle enum/mappings, constrained
   closure source fact і partial unique index; перевірити clean apply та
   concurrency.
3. Реалізувати atomic Issue/full-one-off transitions, signed preview/stale
   checks, idempotency, audit та correction dependency rules.
4. Перевести current/history/negative/report/UI queries на one-active plus
   historical-open-debt model і додати tablet/phone regression coverage.
5. Пройти focused PostgreSQL/domain/command/report/audit/UI gates, повний
   `scripts/validate.sh`, independent review і лише тоді починати Milestone 10.6.

ADR-021 supersedes ADR-014's multiple-lifecycle-active decision and rejection
of the partial unique constraint. It supplements ADR-005, ADR-010, ADR-015,
ADR-016, ADR-018 and ADR-020. ADR-018/020 coverage ordering, exact Payments,
unknown-opening honesty and correction history remain unchanged except for the
explicit lifecycle transitions stated here.
