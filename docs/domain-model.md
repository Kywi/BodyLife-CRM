# BodyLife CRM domain model

## 1. Domain overview

BodyLife CRM v1 - це внутрішня операційна система для одного залу. Її бізнес-модель не про маркетингову CRM, а про щоденний облік рецепції: продати абонемент, знайти клієнта, відмітити візит, прийняти готівку, пояснити нестандартні ситуації й дати власнику надійний денний підсумок.

Зал продає обмежене право користування послугою: клієнт отримує issued Membership на основі MembershipType. У цього абонемента є дата старту, тривалість, ліміт занять і ціна, зафіксовані на момент видачі. Кожен Visit списує одне заняття з конкретного виданого абонемента або фіксується як разовий/пробний сценарій. Календарна активність і залишок занять - різні бізнес-факти: абонемент може бути ще активним по даті, але мати 0 або мінусові заняття; або дата вже минула, але заняття ще лишились. Система має показувати обидва факти, а не ховати їх за одним нечітким статусом.

Перша версія замінює папір і частковий Excel-облік. Основний ланцюжок рецепції: знайти Client за card number, ПІБ або телефоном; побачити поточний стан Membership; записати Visit; прийняти Payment; додати Freeze; застосувати NonWorkingDay; пояснити результат через історію клієнта, Reports і Audit. Основний ланцюжок власника: бачити готівку, візити, абонементи, що скоро закінчуються, малий залишок занять, мінусових клієнтів, клієнтів, які давно не ходили, і хто виконував ключові дії.

Найважливіша доменна межа - Memberships. Visits, Payments, Freezes, NonWorkingDays, UI і Reports не рахують стан абонемента самостійно. Вони створюють source facts або читають публічні Memberships queries. Memberships володіє active status, remaining visits, negative balance, first negative visit date, freeze/non-working overlap, effective end date, extension days і membership warnings.

Домен використовує source facts плюс контрольований derived state. Source facts: issued Membership, Visits, canceled Visits, Payments, one-off negative closures, Freezes, NonWorkingDays, backdated entries, corrections і explicit audited adjustments. Derived state перераховується з цих фактів і має бути пояснюваним. Система не повинна тихо ховати корекцію, переписувати спірну історію або міняти effective end date без джерела причини.

Цей документ описує бізнес-домен і правила, з яких можна писати domain tests без UI. Він свідомо не обирає БД, persistence model, ORM, таблиці або schema design.

## 2. Entities

### Client

Client - це людина, яку зал обліковує як клієнта. Звичайний Client має персональні дані, телефон, опційний card number, коментарі, memberships, visits, payments, freezes і history. Client може існувати без card number, бо рецепція має шукати його по ПІБ, частинах ПІБ, телефону або останніх 4 цифрах телефона.

Мінімальна бізнес-ідентичність:

- внутрішній ідентифікатор клієнта;
- прізвище;
- ім'я;
- по батькові, якщо використовується;
- нормалізований телефон;
- опційний current card number;
- коментар;
- дата створення і Actor, який створив клієнта;
- опційний статус клієнта: активний / неактивний.

Дублікати клієнтів можливі в реальній роботі, але merge clients не входить у v1. Duplicate warnings є частиною домену: дубль current card number блокує створення або зміну, а дубль телефона чи схоже ПІБ попереджає, але може бути підтверджений явно.

### Card number

Card number - це швидкий ідентифікатор для рецепції, але не єдина ідентичність клієнта. У v1 він вводиться вручну. Barcode, QR, NFC, scanner-specific identity і turnstile flows не входять у першу версію.

Правила:

- Client може не мати current card number;
- заповнений current card number є унікальним серед current assignments;
- один card number не може бути current для двох Clients одночасно;
- зміна або перевидача card number є явною audited action;
- історичні card assignments дозволені як доменна історія, але тільки один current assignment може бути активним для номера.

Exact card-number match має пріоритет у пошуку. Якщо збіг неповний, неоднозначний або відсутній, рецепція шукає по ПІБ, частинах ПІБ, нормалізованому телефону або last four phone digits.

### MembershipType

MembershipType - це шаблон продажу абонемента. Ним керує Owner через каталог типів.

Бізнес-поля:

- name;
- duration_days;
- visits_limit;
- price;
- is_active;
- опційний comment.

Lifecycle rules:

- Owner створює, редагує і деактивує MembershipType;
- hard delete заборонений;
- inactive типи недоступні для звичайних нових продажів;
- inactive типи лишаються видимими в history і Reports;
- зміни MembershipType не змінюють уже видані Memberships.

Дитячі або спеціальні продукти моделюються окремими MembershipTypes, а не складною характеристикою Client "дитина". Разові й пробні відвідування можуть бути окремим типом або технічним клієнтом/швидким workflow, але їхні Visits і Payments мають потрапляти в daily report.

### Issued Membership

Issued Membership - це конкретний абонемент, виданий конкретному Client. Він посилається на обраний MembershipType і копіює immutable snapshot на момент видачі:

- type_name;
- duration_days;
- visits_limit;
- price.

Issued Membership також має:

- Client;
- start date;
- base end date;
- effective end date як derived state;
- visits limit snapshot;
- counted visits як derived state;
- remaining visits як derived state;
- negative balance як derived state;
- first negative visit date як derived state;
- extension days як derived state;
- payment relationship;
- status/warnings;
- Actor, який видав абонемент, і recorded time.

Effective end date не редагується напряму як "поле дати". Зміна дати має source reason: Freeze, NonWorkingDay, cancellation/correction, explicit audited adjustment або valid backdated/opening state.

### Visit

Visit - це факт приходу клієнта. Він фіксує бізнес-подію, а не час входу/виходу.

Бізнес-факти:

- Client;
- issued Membership або explicit one-off/trial context;
- visit date / occurred_at;
- Actor, який відмітив візит;
- cancellation state;
- Actor і час скасування;
- опційний cancellation reason/comment.

Counted Visit списує одне заняття з пов'язаного Membership. Canceled Visit лишається в history, але не входить у visit counts, remaining visits, daily visit totals, last-visit calculations, negative balance і first negative visit date.

ADR-014 уточнює allocation policy для v1:

- Client може мати кілька lifecycle-active issued Memberships;
- membership Visit завжди зберігає explicit selected Membership, а при кількох
  date-active candidates UI/server не обирають newest/first автоматично;
- Visit не може передувати `start_date`; expired Membership можна вибрати лише
  явно з current-state warning acknowledgement;
- без date-active Membership Actor явно обирає expired Membership або
  non-membership `one_off`/`trial` context;
- one-off/trial Visit не створює consumption і не змінює Memberships state;
- active Freeze, що inclusive покриває Visit business date, блокує membership
  Visit до correction/cancellation Freeze; warning override у v1 немає.

Для deterministic recalculation active counted Visits ordered by `occurred_at`,
потім `recorded_at`, потім stable Visit id. Cancellation перебудовує state з
решти active counted facts.

### Payment

Payment фіксує готівку, яку отримав зал. У v1 метод оплати один: cash. Online payments, bank integrations, terminals, full POS і complex accounting не входять у scope.

Бізнес-факти:

- Client;
- issued Membership або one-off/trial/negative-closure context;
- amount;
- cash method;
- payment date / occurred_at;
- Actor, який прийняв оплату;
- comment;
- cancellation/correction state, якщо застосовано.

Partial-payment accounting не моделюється як складний сценарій v1: у звичайному продажі Membership вважається оплаченим цілком. Payment corrections або cancellations впливають на daily cash report і мають бути audited.

### Freeze

Freeze - це source fact для індивідуального продовження Membership. Він має inclusive date range і reason/comment.

Бізнес-факти:

- Client;
- issued Membership;
- start date;
- end date;
- calculated day count;
- reason/comment;
- Actor, який додав Freeze;
- recorded time;
- cancellation state.

Active Freeze додає календарні дні до extension source days для Membership. Canceled Freeze додає 0 extension days, але лишається в history і Audit.

### NonWorkingDay

NonWorkingDay - це source fact для днів, коли зал не працював. Це може бути один день або inclusive period.

Бізнес-факти:

- date або date range;
- day count;
- reason: holiday, New Year, repair, technical day або other;
- Actor, який створив запис;
- recorded time;
- affected membership summary/history;
- cancellation state, якщо неробочий період додали помилково.

Accepted ADR-012 уточнює повноваження v1: додавання і скасування NonWorkingDays є Owner-only, хоча ранні вимоги згадували Owner або Admin. Домен все одно має показати кількість affected active memberships перед підтвердженням і зберегти reason кожного extension.

### Reports

Reports - це query/report views поверх canonical source facts і Memberships queries. Reports не є окремою доменною правдою і не копіюють membership formulas.

V1 reports:

- daily cash/visits report;
- memberships ending soon з `days_left <= 7`;
- memberships with low remaining visits з `remaining_visits <= 2`;
- negative clients;
- inactive clients з thresholds 14, 30 і 60 days.

Кожен report total має drill-down до source records. Corrections можуть змінювати live totals, навіть після day close/reconciliation, але ці зміни мають бути видимі через report history/drill-down і Audit.

### Audit

Audit - це append-only business history. Він окремий від technical logs і потрібен для пояснення спірних visits, payments, freezes, cancellations, extensions, backfilled entries і settings changes.

Audit captures:

- actor/account;
- role;
- session/device;
- action type;
- entity type/id;
- related ids;
- occurred_at;
- recorded_at;
- before/after або domain summary;
- reason/comment;
- request/correlation id, якщо доступний.

Audit обов'язковий для payments, visits, freezes, non-working days, issue/cancel membership, backdated entries, corrections, MembershipType/settings changes і card reassignment/change.

### User/Actor

User/Actor - це accountable system identity, яка виконує command.

V1 actor types:

- Owner account;
- named Admin account;
- shared Reception/Admin account.

Owner може виконувати admin actions і володіє небезпечними або catalog-level actions. Named Admin виконує щоденний reception workflow. Shared Reception/Admin існує для робочого девайса рецепції, тренера на заміні або іншої довіреної людини, але audit має чесно показувати, що фізична особа невідома поза shared session.

Permissions summary:

- Owner-only: create/edit/deactivate MembershipType, add/cancel NonWorkingDays, dangerous mass actions, service settings;
- Admin + Owner: create/edit Client, issue Membership, take Payment, record Visit including negative, cancel mistaken Visit, add/cancel Freeze, view daily cash report;
- Admin + Owner current-day: correct/cancel Payment або Freeze with audit reason;
- after day close/reconciliation: corrections є Owner-only або потребують explicit owner-approved policy.

## 3. Invariants

- Client може існувати без card number.
- Заповнений current card number є унікальним серед current card assignments.
- Card number не може бути current для двох Clients одночасно.
- Card-number change або reassignment є explicit audited action.
- MembershipType редагується для майбутніх продажів, але не hard-deletable.
- Issued Membership зберігає immutable snapshot MembershipType: name, duration, visit limit і price.
- Зміна MembershipType ніколи тихо не змінює вже issued Membership.
- Memberships є єдиним owner для active status, remaining visits, negative balance, first negative visit date, effective end date, extension days і membership warnings.
- UI, Reports, Visits, Payments, Freezes і NonWorkingDays читають membership state через Memberships public queries, а не рахують його самі.
- Membership end date inclusive: membership активний по даті, якщо `today <= effective_end_date`.
- Date activity і visit balance - різні факти. Membership може бути active by date з 0 або negative visits, або expired by date із remaining visits.
- Effective end date є derived, а не directly edited. Кожна зміна має source reason.
- Counted visits виключають canceled visits.
- Remaining visits може бути negative.
- Negative balance є membership state, а не separate financial debt ledger у v1.
- First negative visit date рахується як дата першого counted Visit, після якого running remaining visits став меншим за 0.
- Negative closure є explicit. New Membership може стартувати з first negative visit date, або negative visits можуть закриватися one-off payments/visits, але система не приховує це рішення.
- Freeze ranges і NonWorkingDay ranges inclusive.
- Freeze і NonWorkingDay extension походять із source records.
- Overlap Freeze і NonWorkingDay рахується як union calendar days. Один календарний день автоматично дає максимум 1 extension day.
- Canceled Freezes і canceled NonWorkingDays не додають extension days після recalculation.
- Будь-який command, який змінює visits, payments, freezes, non-working days, issued memberships, backfill/opening state або corrections, тригерить membership recalculation там, де це релевантно.
- Corrections і cancellations не видаляють business history. Вони додають correction/cancellation facts і Audit entries.
- Reports є query/report views поверх canonical source facts і Memberships queries.
- Daily report totals виключають canceled visits і canceled payments.
- Later corrections можуть змінювати live report totals, але correction має бути видимою через drill-down і Audit.
- Backdated entries використовують ті самі domain commands, що й current entries, і мають `occurred_at` та `recorded_at`.
- Direct database edits, synthetic fake history і unmarked backdated entries не входять у доменну модель.

## 4. Lifecycles

### Client and card lifecycle

1. Client створюється з мінімальними identity data.
2. Опційний current card number призначається, якщо він унікальний.
3. Search знаходить Client по card number, ПІБ, телефону або last four phone digits.
4. Client data може коригуватися з duplicate warnings і Audit.
5. Card number може бути змінений або перевиданий тільки explicit audited action.
6. Client може стати operationally inactive, але historical memberships, visits, payments і audit лишаються readable.

### MembershipType lifecycle

1. Owner створює MembershipType.
2. Owner редагує future catalog values, якщо потрібно.
3. Owner деактивує MembershipType, коли його більше не продають.
4. Hard delete заборонений.
5. Existing issued Memberships продовжують використовувати issue-time snapshot.
6. Якщо вже issued Membership треба виправити через помилку в типі, це explicit membership correction workflow, а не side effect редагування MembershipType.

### Issued Membership lifecycle

1. Actor відкриває Client і обирає active MembershipType.
2. System copies MembershipType snapshot.
3. Actor обирає start date.
4. Membership отримує base end date, effective end date, visit limit і initial derived state.
5. Payment записується в ordinary cash flow.
6. Visits consume remaining visits і можуть довести balance до 0 або negative.
7. Freeze і NonWorkingDay source records extend effective end date через recalculation.
8. Warnings derived для ending soon, low visits, zero visits, negative visits або expired-by-date state.
9. Membership може завершитись by date, by visits, by both, або бути canceled/corrected.
10. Будь-яка cancellation, correction або backdated entry перераховує derived state from source facts.

### Visit lifecycle

1. Actor records Visit for Client and Membership або one-off/trial context.
2. Visit стає counted, якщо не canceled.
3. Membership remaining visits і negative state recalculated.
4. Daily visit report includes Visit for its occurred date.
5. Якщо Visit помилковий, Actor cancels it with reason/comment when required.
6. Canceled Visit stays in history, is excluded from counts, and triggers recalculation.

### Payment lifecycle

1. Actor records cash Payment for Client and Membership або one-off/trial/negative-closure context.
2. Payment appears in client history and daily cash report for its occurred date.
3. Якщо Payment corrected або canceled, correction is explicit, audited, and reflected in daily report totals.
4. After day close/reconciliation, correction follows Owner-only або explicit owner-approved policy.

### Freeze lifecycle

1. Actor adds Freeze to issued Membership with inclusive start and end dates.
2. System derives day count and recalculates effective end date.
3. Freeze is visible in client history.
4. Якщо Freeze помилковий, Actor cancels it with reason/comment.
5. Canceled Freeze contributes no extension days after recalculation but remains auditable.

### NonWorkingDay lifecycle

1. Owner creates NonWorkingDay або period with reason.
2. System previews affected active memberships.
3. Owner confirms application.
4. Memberships recalculates affected effective end dates using union calendar-day rule.
5. Client profiles show non-working extension reason.
6. Якщо period помилковий, Owner cancels it або applies explicit audited correction.
7. Reports and histories remain explainable after recalculation.

### Report lifecycle

1. Report requested for date або threshold.
2. Report queries canonical records and Memberships derived state.
3. Report shows totals and drill-down records.
4. Correction/cancellation changes live report results.
5. Якщо день був reconciled, later change visible as changed-after-reconciliation through report drill-down and Audit.

### Audit lifecycle

1. Command succeeds.
2. System appends business audit entry with Actor, action, entity, related ids, occurred/recorded time, summary, and reason/comment where needed.
3. Corrections append additional entries instead of editing or deleting previous ones.
4. Technical logs may reference request/correlation ids, but they are not business audit.

## 5. Calculation rules

### Date conventions

Усі бізнесові date ranges у цьому домені inclusive, якщо майбутній ADR не скаже інакше.

Для domain tests `duration_days` трактується як кількість активних календарних днів включно зі `start_date`.

```text
base_end_date = start_date + duration_days - 1 day
active_by_date = today <= effective_end_date
```

Якщо зал підтвердить legacy Excel convention, де `start_date + duration_days` є останнім активним днем, цю арифметику потрібно змінити явно. Inclusive-end invariant лишається: final end date є останнім днем, коли Client може використати Membership.

### Membership derived state

Memberships recalculates derived state from source facts:

- issued Membership snapshot;
- counted Visits;
- canceled Visits;
- Payments and explicit one-off negative closures;
- active/canceled Freezes;
- active/canceled NonWorkingDays;
- backdated entries;
- corrections;
- explicit audited adjustments.

Core derived values:

```text
counted_visits = visits linked to or explicitly covered by the membership
                 where visit is not canceled

remaining_visits = visit_limit_snapshot - counted_visits

negative_balance = max(0, -remaining_visits)

first_negative_visit_date =
  date of the first counted visit that makes running remaining_visits < 0

extension_days =
  count of unique calendar days contributed by active Freeze and
  applicable NonWorkingDay source records

effective_end_date = base_end_date + extension_days + explicit_adjustment_days
```

`remaining_visits` є signed value і може показуватися як `-1`, `-2` тощо. `negative_balance` - positive size of that negative state для reports і rules.

### Remaining visits and cancellation

Visit списує заняття тільки тоді, коли він counted. Canceling Visit removes it from `counted_visits`, recalculates `remaining_visits`, and may clear or move `first_negative_visit_date`.

Example:

```text
visit_limit_snapshot = 8
counted_visits before cancellation = 9
remaining_visits = -1

cancel one counted visit
counted_visits after cancellation = 8
remaining_visits = 0
negative_balance = 0
first_negative_visit_date = none
```

### Negative visits and first negative date

System does not block Visit when remaining visits are 0. New Visit creates negative membership state.

Якщо running balance стає negative кілька разів, `first_negative_visit_date` - earliest counted Visit date у поточному recalculated fact set. Якщо original first negative Visit canceled, value recalculated to next counted negative-causing Visit або cleared.

Negative visits не моделюються як separate financial debt ledger у v1. Це visible membership state, який має закриватися explicit workflow.

### New membership after negative visits

Коли Client має negative balance і Actor issues new Membership, system must warn:

```text
Client has negative visits. Check the start date of the new membership.
```

Actor має бачити `first_negative_visit_date`. New Membership може start on that date, щоб покрити visits, already used in negative state. Це має бути explicit: workflow records that negative visits are covered by new Membership or by one-off negative closure facts. Old negative state must not disappear just because new Payment exists.

Domain expectation:

- якщо new Membership covers negative visits, relevant negative Visits are counted against or explicitly covered by new Membership from `first_negative_visit_date`;
- якщо one-off closure chosen, closure fact explains why negative balance is no longer open;
- якщо neither chosen, negative balance remains visible.

### Freeze extension

Freeze day count inclusive:

```text
freeze_days = freeze_end_date - freeze_start_date + 1
```

Active Freeze contributes those calendar days as extension source days for related Membership. Canceled Freeze contributes zero days.

### Non-working day extension

NonWorkingDay day count inclusive:

```text
non_working_days = period_end_date - period_start_date + 1
```

NonWorkingDay source record extends affected active memberships for applicable calendar days when the gym was closed. Reason must remain visible in client profile/history. Owner should see number of affected memberships before confirmation.

### Freeze and NonWorkingDay overlap

Extension source days de-duplicated by calendar date.

Example:

```text
freeze:          2026-01-01 to 2026-01-03
non-working:     2026-01-02 to 2026-01-03
unique days:     2026-01-01, 2026-01-02, 2026-01-03
extension_days:  3, not 5
```

Якщо бізнес хоче double extension для конкретного exceptional day, це не automatic overlap behavior. Це explicit audited adjustment with reason.

### Reports

Daily report for selected date:

```text
daily_visit_count = counted, not-canceled Visits with occurred_at on date
daily_payment_count = active, not-canceled Payments with occurred_at on date
daily_cash_sum = sum of active, not-canceled cash Payments with occurred_at on date
```

Ending soon:

```text
days_left <= 7
```

Low remaining visits:

```text
remaining_visits <= 2
```

Negative clients:

```text
negative_balance > 0
```

Inactive clients:

```text
today - last_counted_visit_date >= selected threshold
```

Canceled Visits do not count as last visit. Clients with no visits may be shown separately або at the end of inactive lists.

### Backdated entries

Backdated visits, payments, freezes, memberships and opening states use domain commands. They must have:

- actual business date/time as `occurred_at`;
- system entry time as `recorded_at`;
- actor/account;
- reason/comment;
- marker such as `manual_backfill` або `paper_fallback`.

Recalculation uses business occurrence where relevant. Audit must show that source fact was entered later.

## 6. Correction and cancellation rules

Correction означає, що новий explicit fact змінює бізнес-інтерпретацію попереднього fact. Cancellation означає, що попередній fact лишається видимим, але виключається з active calculations там, де це визначено доменом.

General rules:

- не hard-delete business history через normal workflows;
- append Audit for every correction/cancellation;
- require reason/comment for dangerous or dispute-prone corrections;
- preserve before/after або domain summary;
- recalculate impacted membership state after successful correction;
- update report results through canonical facts, not by manually editing report totals.

Visit cancellation:

- Visit remains in history as canceled;
- it no longer consumes a visit;
- it is removed from daily visit count;
- it is ignored for last-visit and inactive-client reports;
- remaining visits, negative balance, first negative date and warnings are recalculated.

Payment cancellation/correction:

- Payment remains visible as canceled або corrected;
- daily payment count and cash sum are recalculated for affected occurred date;
- якщо date або amount changes, both old and new affected daily reports remain explainable;
- after day close/reconciliation, correction follows Owner-only або explicit owner-approved policy.

Freeze cancellation:

- Freeze remains visible as canceled;
- its dates no longer contribute extension days;
- effective end date and warnings are recalculated;
- if it overlapped NonWorkingDays, union recomputed, not patched manually.

NonWorkingDay cancellation:

- NonWorkingDay remains visible as canceled/corrected;
- affected memberships recalculated;
- client histories still explain that extension was added and later canceled/corrected;
- if full automatic cancellation is not available in first implementation, any manual fix must be explicit audited adjustment, not silent end-date edit.

Membership cancellation/correction:

- canceling issued Membership is explicit and audited;
- correcting issue-time snapshot, start date або opening state requires correction workflow;
- changes to MembershipType catalog never silently rewrite issued Memberships;
- recalculation must include visits, payments, freezes, non-working days and negative closure facts related to corrected membership.

Backdated correction:

- backdated facts allowed only through domain commands;
- they must be marked as manual backfill або paper fallback when applicable;
- they trigger same validation and recalculation as current-day facts;
- reports for historical dates may change after backdated entry and must show why through Audit.

## 7. Edge case matrix

| Area | Edge case | Expected domain behavior | Recalculation / audit / report impact |
|---|---|---|---|
| Negative visits | Visit recorded when remaining visits are 0 | Visit is allowed; remaining visits becomes `-1`; negative warning is visible | Membership recalculates `remaining_visits`, `negative_balance`, `first_negative_visit_date`; daily visits include Visit |
| Negative visits | Client goes to `-2` or lower | Each counted Visit keeps reducing signed remaining visits | `negative_balance` grows; first negative date remains earliest current negative-causing Visit |
| Negative visits | First negative Visit is canceled | Canceled Visit no longer counts | Remaining visits increases; first negative date moves to next negative-causing Visit or clears |
| Negative visits | New Membership issued after negative visits | Actor sees warning and first negative date; start date may be first negative date | Negative coverage must be explicit; old negative state is not hidden by payment alone |
| Negative visits | Negative visits closed by one-off workflow | One-off closure is explicit and visible | Membership state changes only through closure fact; Audit explains closure reason |
| Negative visits | Membership expired by date but visits remain | System shows both: expired by date and remaining visits | Admin decision is outside automatic calculation; Reports use Memberships state |
| Negative visits | Membership active by date but visits are 0 | System shows 0 visits and allows negative Visit if business permits | Recording Visit creates negative state and warning |
| Cancellations | Normal counted Visit is canceled | Visit remains in history but is excluded from active counts | Remaining visits and daily visit report recalculate; Audit captures actor/reason |
| Cancellations | Visit that created negative balance is canceled | Negative state recalculated from remaining counted visits | `negative_balance` may clear; `first_negative_visit_date` may change |
| Cancellations | Payment canceled same day | Payment remains as canceled; cash total excludes it | Daily cash report count/sum changes; Audit captures correction |
| Cancellations | Payment date or amount corrected after day close | Correction allowed only by policy; old and new report dates remain explainable | Reports change live totals; Audit/drill-down shows changed-after-reconciliation |
| Cancellations | Issued Membership created with wrong MembershipType snapshot | Catalog edit does not fix it | Explicit membership correction required; Audit preserves before/after |
| Freeze | Freeze range is 2026-01-10 to 2026-01-12 | Range inclusive and contributes 3 calendar days | Effective end date extends by 3 days unless overlap de-duplicates days |
| Freeze | Freeze is canceled | Freeze contributes 0 extension days | Effective end date recalculates; client history shows canceled Freeze |
| Freeze | Two Freeze records overlap each other | One calendar day should not inflate extension twice without explicit adjustment | Extension uses unique calendar days; Audit shows both source records |
| Freeze | Backdated Freeze entered after visits already exist | Backdated source fact valid only through command with reason | Effective end date recalculates from source facts; Audit records occurred/recorded times |
| NonWorkingDay | Owner adds single non-working day | Affected active memberships extend for that calendar day | Client histories show non-working reason; report/client profile read recalculated state |
| NonWorkingDay | Owner adds non-working period | Inclusive period contributes applicable closed calendar days | Preview affected memberships before confirmation; Audit records owner and reason |
| NonWorkingDay | Non-working period added by mistake | Owner cancels/corrects it | Affected memberships recalculate; Audit and histories show add plus cancel/correction |
| NonWorkingDay | Admin tries to add/cancel NonWorkingDay | ADR-012 makes this Owner-only | Command rejected by permissions; Audit may capture denied attempt if supported |
| Freeze + NonWorkingDay | Freeze 2026-01-01..03 and NonWorkingDay 2026-01-02..03 | Unique extension days are 01, 02, 03 | Extension is 3 days, not 5 |
| Freeze + NonWorkingDay | Overlap discovered after one source is canceled | Union recomputed from remaining active source records | Effective end date may change; no manual patch to report totals |
| Backdated entries | Paper fallback visits/payments entered next day | `occurred_at` is business date; `recorded_at` is entry date | Historical daily report changes; Audit marks `paper_fallback` |
| Backdated entries | Opening state for active Membership has current remaining visits but incomplete old history | Opening state is explicit source fact, not fake generated history | Membership state can start from declared opening facts; Audit records source and reason |

## 8. Domain test scenarios

These scenarios should be testable through domain/application commands and queries without UI.

1. Inclusive end date
   - Given Membership starts on 2026-01-01 with `duration_days = 30`.
   - When derived state is calculated.
   - Then base end date is 2026-01-30 and Membership is active on 2026-01-30 but not active by date on 2026-01-31.

2. Remaining visits from counted visits
   - Given issued Membership with visit limit 8 and 3 counted Visits.
   - When membership state is queried.
   - Then `remaining_visits = 5`, `negative_balance = 0`, and no first negative date exists.

3. Canceled Visit is excluded
   - Given issued Membership with visit limit 8, 4 Visits, and one Visit canceled.
   - When membership state is recalculated.
   - Then counted visits are 3 and `remaining_visits = 5`.

4. Negative Visit is allowed
   - Given issued Membership with visit limit 8 and 8 counted Visits.
   - When another Visit is recorded.
   - Then `remaining_visits = -1`, `negative_balance = 1`, and `first_negative_visit_date` is that Visit date.

5. Multiple negative Visits keep first negative date
   - Given first negative Visit happened on 2026-10-02 and another negative Visit happened on 2026-10-05.
   - When membership state is recalculated.
   - Then `first_negative_visit_date = 2026-10-02` and `negative_balance = 2`.

6. Canceling first negative Visit recalculates first negative date
   - Given negative Visits on 2026-10-02 and 2026-10-05.
   - When 2026-10-02 Visit is canceled.
   - Then first negative date becomes 2026-10-05, or clears if no remaining Visit creates negative balance.

7. New Membership after negative visits warns and can start at first negative date
   - Given Client has `negative_balance > 0` and `first_negative_visit_date = 2026-10-02`.
   - When Actor issues new Membership.
   - Then command exposes warning and allows start date 2026-10-02.
   - And if Actor chooses to cover negative visits with new Membership, coverage is explicit and auditable.

8. New Payment alone does not hide negative visits
   - Given Client has negative visits.
   - When cash Payment is recorded without selecting negative coverage by new Membership or one-off closure.
   - Then negative state remains visible.

9. One-off negative closure is explicit
   - Given Client has `negative_balance = 1`.
   - When Actor records accepted one-off negative closure.
   - Then Membership state reflects closure according to explicit closure rule and Audit explains it.

10. Freeze extension is inclusive
   - Given Membership effective end date before extension is 2026-01-31.
   - When Freeze from 2026-01-10 to 2026-01-12 is added.
   - Then extension contributes 3 days and effective end date becomes 2026-02-03 unless other source records affect same days.

11. Canceled Freeze no longer extends
   - Given Freeze contributed 3 extension days.
   - When Freeze is canceled.
   - Then those 3 days are removed from extension calculation and Audit shows cancellation.

12. NonWorkingDay extends affected memberships
   - Given active Membership and NonWorkingDay period 2026-01-01 to 2026-01-03.
   - When Owner confirms period.
   - Then applicable closed days become extension source days and membership history shows reason.

13. Freeze and NonWorkingDay overlap uses union days
   - Given Freeze 2026-01-01 to 2026-01-03 and NonWorkingDay 2026-01-02 to 2026-01-03.
   - When extension days are calculated.
   - Then extension is 3 days, not 5.

14. Direct effective end date edit is rejected
   - Given Actor tries to change effective end date without source reason.
   - When command is validated.
   - Then it fails, or must be converted into explicit audited adjustment with reason.

15. Visit cancellation that created a minus clears the minus
   - Given visit limit is 8, counted visits are 9, and remaining visits are `-1`.
   - When one counted Visit is canceled.
   - Then remaining visits becomes 0 and negative state clears.

16. Backdated Visit triggers recalculation
   - Given Membership has already been viewed with remaining visits 2.
   - When paper fallback Visit with earlier `occurred_at` is entered today.
   - Then remaining visits recalculates to 1 and Audit shows both `occurred_at` and `recorded_at`.

17. Backdated Freeze triggers recalculation
   - Given Membership has known effective end date.
   - When backdated Freeze is entered through domain command.
   - Then effective end date recalculates and Freeze is marked with manual_backfill or paper_fallback when applicable.

18. Payment cancellation changes daily report
   - Given daily report includes one cash Payment.
   - When that Payment is canceled.
   - Then daily payment count and cash sum decrease and drill-down shows canceled Payment.

19. Reports and client profile use same membership state
   - Given Membership has low remaining visits and non-working extension.
   - When client profile and low-remaining report query state.
   - Then both show same `remaining_visits` and `effective_end_date` from Memberships.

20. MembershipType snapshot is immutable for issued Membership
   - Given MembershipType price or visit limit is edited after issuing Membership.
   - When issued Membership is queried.
   - Then it still uses issue-time snapshot, and catalog edit Audit is separate from membership history.

21. Card number uniqueness blocks duplicate current assignment
   - Given Client A has current card number `123`.
   - When Actor tries to assign current card number `123` to Client B.
   - Then command fails and no duplicate current assignment exists.

22. Backfilled opening state is honest
   - Given active Membership history is incomplete during manual backfill.
   - When Actor creates opening state with start date, snapshot, remaining visits or negative balance, known end/extension state, reason and source.
   - Then membership queries can calculate from that opening fact and Audit marks it as manual backfill.

## 9. Open implementation questions

Inclusive date arithmetic is accepted by ADR-005 and locked by tests. Multiple
Memberships, Visit allocation/no-active behavior, same-day ordering and Visit
during Freeze are accepted by ADR-014.

1. Define whether NonWorkingDay period extends Membership only for overlapping active calendar days or for full period once any overlap exists.
2. Define validation for Freeze ranges: can Freeze start before Membership start, after Membership end, or outside current active period?
3. Specify exact one-off negative closure behavior: whether it consumes one-off MembershipType, payment-only closure, or another explicit domain fact.
4. Define which correction/cancellation actions always require reason/comment and which can use optional comments.
5. Define day close/reconciliation policy: who closes the day, what owner approval means after close, and how changed-after-close is labeled in Reports.
6. Choose standard inactive-client default threshold while keeping 14, 30 and 60 days available.
7. Define whether denied permission attempts are business-audited or only technically logged.
8. Decide how much historical card-assignment history is visible in v1 beyond current card number and Audit trail.
