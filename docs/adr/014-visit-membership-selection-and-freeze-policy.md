# ADR-014: Visit membership selection and freeze policy

## Статус

Accepted - 2026-07-14.

## Контекст

Milestone 6 не може безпечно реалізувати `MarkVisit`, доки не визначено:

- чи може Client мати кілька чинних issued Memberships одночасно;
- як Visit вибирає Membership при overlap;
- що робити, якщо немає Membership, активного на business date;
- чи можна списати Visit під час active Freeze;
- як детерміновано знайти first negative Visit після backfill або cancellation.

Автоматичний вибір "останнього" Membership небезпечний: він може списати
заняття не з того snapshot, приховати старий negative state або зробити profile,
report і audit неузгодженими. Жорстка заборона кількох lifecycle-active
Memberships теж неприйнятна для v1, бо backdated issue та новий Membership після
negative visits не повинні мовчки закривати попередній source state.

## Варіанти

- Дозволити тільки один lifecycle-active Membership на Client.
- Дозволити кілька Memberships і автоматично списувати з найновішого.
- Дозволити кілька Memberships, але вимагати explicit selection для Visit.
- Без date-active Membership автоматично списувати з останнього expired.
- Без date-active Membership вимагати explicit expired або one-off/trial choice.
- Visit під час Freeze блокувати, дозволяти з warning або дозволяти без обмежень.

## Рішення

### Кілька чинних Memberships

BodyLife CRM v1 дозволяє кілька lifecycle-active issued Memberships для одного
Client. `IssueMembership` не закриває і не змінює status іншого Membership
автоматично. Старий negative або overlapping state залишається видимим, доки
його не змінить окремий explicit correction/coverage/closure workflow.

Public Memberships query продовжує повертати `none`, `single` або `ambiguous`.
При `ambiguous` немає canonical current Membership і жоден consumer не має
права обирати перший, останній або "найкращий" рядок самостійно.

### Membership visit selection

Membership Visit завжди має explicit `membership_id` у `MarkVisit`. Навіть якщо
UI може попередньо вибрати єдиний підхожий Membership, server command отримує і
повторно перевіряє конкретний id.

Для Visit business date вводиться окрема eligibility-перевірка, якою володіє
Memberships:

- lifecycle status має бути `active`, а не `canceled` або `corrected`;
- `occurred_at`/business date не може бути раніше `start_date`;
- Membership у межах `start_date..effective_end_date` є ordinary date-active
  candidate;
- expired Membership після `effective_end_date` можна вибрати тільки явно і з
  current-state `expired` acknowledgement;
- future-start Membership не можна використати до `start_date` навіть з
  acknowledgement.

Якщо date-active candidate один, UI може preselect його. Якщо candidates кілька,
UI має показати snapshot/name, start/effective-end dates, remaining visits і
warnings та вимагати deliberate choice. Server ніколи не infers selection з
ordering.

Zero або negative remaining visits не блокують Membership Visit, але потребують
explicit current-state acknowledgement. Якщо одночасно діють `expired`, `zero`
або `negative` conditions, command має підтвердити кожну required condition;
один generic checkbox не замінює server-derived required warning set.

### Visit без date-active Membership

Немає автоматичного default. Actor має зробити одну явну дію:

1. вибрати lifecycle-active expired Membership і підтвердити `expired` та інші
   required warnings; або
2. вибрати non-membership `one_off` чи `trial` context.

Якщо Client не має жодного selectable issued Membership, доступний тільки
explicit one-off/trial path. One-off/trial Visit:

- створює canonical `visits` row;
- не створює `visit_consumptions`;
- не змінює Memberships state і не створює "мінус без Membership";
- входить у daily visit report та business audit;
- може використовувати звичайного Client або dedicated technical Client для
  невідомих разових/пробних відвідувачів.

### Visit під час Freeze

Membership Visit блокується, якщо active Freeze цього Membership inclusive
покриває Visit business date. V1 не має warning override для цієї умови.
`MarkVisit` повертає stable `visit_during_freeze` без source fact, consumption,
recalculation або business audit success entry.

Actor має спочатку cancel/correct Freeze з його власним reason/audit workflow
або явно записати one-off/trial Visit, який не споживає frozen Membership. Visit
не скорочує і не скасовує Freeze автоматично. Та сама перевірка застосовується
до normal, backdated і paper-fallback commands за їхнім `occurred_at`.

Це не дозволяє одночасно отримати counted membership Visit і extension day за
той самий frozen calendar day та зберігає source facts пояснюваними.

### Ordering і recalculation

Counted Visits для Memberships recalculation впорядковуються за:

1. `occurred_at`;
2. server-set `recorded_at`;
3. stable Visit id як final tie-break.

Memberships починає з canonical baseline: snapshot visit limit для повної native
history або signed declared remaining value з active opening state, коли старі
факти неповні. Calculator віднімає тільки active counted Visit facts, які не
входять у цей opening baseline; canceled Visit не бере участі в ordering.

`first_negative_visit_id` і `first_negative_visit_date` вказують на перший
ordered canonical Visit, після якого running remaining visits переходить нижче
нуля. Якщо opening baseline уже negative і historical Visit source відсутній,
first-negative metadata лишається unknown, а не прив'язується до першого нового
Visit. Cancellation перераховує state з решти applicable active counted facts.

### Transaction і ownership

Visits володіє Visit/consumption/cancellation source facts. Memberships володіє
eligibility, warning requirements і всіма calculated values.

`MarkVisit` має в одній PostgreSQL transaction:

- авторизувати Actor і перевірити idempotency;
- lock selected Membership state/source rows та relevant Freeze rows;
- повторно перевірити ownership, eligibility і acknowledgements;
- створити Visit та optional counted consumption;
- синхронно перерахувати selected Membership;
- append `visit.marked` audit;
- повернути Client/Profile canonical reread target.

## Відхилено для v1

- partial unique constraint "one active Membership per Client";
- automatic newest/oldest/best Membership selection;
- implicit fallback to expired Membership;
- membershipless negative balance;
- counted Visit on a frozen Membership with override;
- automatic Freeze shortening/cancellation from `MarkVisit`;
- UI-owned eligibility або membership formulas.

## Наслідки

- Overlap лишається explainable і не ховає negative history.
- Reception має один додатковий explicit choice тільки в ambiguous/risky cases.
- One-off/trial visits не забруднюють Memberships state.
- Freeze correction залишається окремим audited workflow.
- Milestone 5 може завершити pure Visit-source calculation contract/tests без
  створення Visit persistence.
- Milestone 6 отримує стабільні selection, warning, locking і error contracts.

## Що це означає для реалізації

- Додати Memberships-owned pure Visit eligibility/calculation inputs і tests до
  Visit schema/commands.
- `GetClientMembershipStates` або окремий public query має віддати достатньо
  candidate data, але не обирати при `ambiguous`.
- `MarkVisit` contract має розрізняти `membership`, `one_off`, `trial`, вимагати
  `membership_id` тільки для membership kind і не приймати його для non-membership
  kinds.
- Додати stable `visit_during_freeze` та typed warning acknowledgements.
- PostgreSQL tests мають покрити multiple candidates, expired selection,
  future-start rejection, no-membership contexts, Freeze blocking, row locks,
  idempotency, cancellation ordering і transaction rollback.
- Playwright tablet/phone flow має перевірити explicit selection і warnings без
  silent auto-selection.
