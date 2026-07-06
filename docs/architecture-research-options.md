# Архітектурне дослідження для першої веб-версії BodyLife CRM

Дата: 2026-07-06  
Оновлено: з відповідями власника/проєкту  
Формат: research brief  
Статус: shortlist, не фінальний вибір  
Обмеження: без вибору мови програмування, бази даних або фреймворку

Цей Markdown-файл є зручною редагованою версією дослідження. HTML-перегляд лежить поруч: [architecture-research-options.html](architecture-research-options.html).

## Короткий висновок

Найсильніший напрямок для першої версії:

- **Modular monolith**: один застосунок і один deploy, але з чіткими межами модулів.
- **Hybrid server-rendered UI**: сторінки рендеряться сервером, а пошук, швидкі дії й попередження інтерактивні.
- **Окремий business audit**: історія дій для спорів не змішується з технічними application logs.
- **V1 без міграції та offline-first**: старт зі стерильної бази, папір як fallback при втраті інтернету.

## 1. Контекст і драйвери

BodyLife CRM у першій версії має замінити поточний ручний облік абонементів на папері та частково в Excel. Основний ланцюжок:

```text
клієнт
-> номер картки або пошук
-> абонемент
-> візит
-> залишок занять або мінус
-> заморозки / неробочі дні
-> готівкова оплата
-> історія
-> денний звіт
-> контроль клієнтів, які давно не ходили
```

Важливо, що це не маркетингова CRM і не велика gym SaaS-платформа. Успіх v1 - коли адміністратор і власник перестають вручну рахувати абонементи на папері, а система стає основним місцем обліку.

Архітектурні драйвери:

- швидкий пошук клієнта на рецепції;
- надійні формули абонементів, мінусів, заморозок і неробочих днів;
- прозора історія для спірних ситуацій;
- денний контроль готівки й відвідувань;
- простий backup/restore;
- низька операційна складність;
- модульність без enterprise-overengineering.

Web app уже прийнятий як базовий напрямок. Ризики web app порівняно з native/desktop: залежність від браузера й мережі, слабший доступ до пристроїв, складніший offline-mode. Для v1 це прийнятно, бо очікується робочий планшет/телефон, доступ власника з телефону і стабільний інтернет.

## 2. Уточнення після відповідей

Частина ризиків знята: система може стартувати простіше, але абонементи треба моделювати уважніше.

| Питання | Уточнена відповідь | Архітектурний наслідок |
|---|---|---|
| Типи абонементів і ціни | Заповнюються динамічно, можуть додаватися нові. Не видаляються. Редагування потрібне тільки для виправлення помилок. | `MembershipType` має бути керованою довідниковою сутністю з деактивацією замість видалення і audit trail для змін. |
| Дата завершення | Дата завершення є останнім активним днем. | Фіксуємо правило `active if today <= end_date`. Це треба винести в доменне правило й покрити тестами. |
| Ручне коригування дати завершення | Не потрібне. | Краще не давати прямого редагування `end_date`. Дата має змінюватися через зрозумілі причини: freeze, non-working day, new membership, cancellation. |
| Мінусові заняття | Дуже часті, не є проблемою. Клієнт може дізнатись про `-1` вже на рецепції. | Negative balance - core feature, не edge case. UI має явно показувати мінус, дату першого мінусового заняття і варіанти закриття. |
| Закриття мінусів | Мінус може покриватися новим абонементом зі стартом на дату першого мінусового заняття або кількома разовими оплатами. | Потрібен явний use case "закрити мінус": новим абонементом або разовими візитами/оплатами. Система не повинна автоматично приховувати це рішення. |
| Міграція | У v1 працюємо зі стерильною базою. Старих клієнтів з активними абонементами можна додати пізніше, якщо знадобиться. | Import не входить у v1. Але модель не повинна заважати ручному створенню клієнта з уже активним абонементом і датою старту з минулого. |
| Тренери | Окремого доступу немає. Є один девайс на адмін-стійці, яким користуються адміни або довірений тренер за відсутності адміна. | Trainer role прибирається з v1. Потрібні `Owner`/`Admin` і, можливо, session-level audit, щоб було видно, під чиїм доступом виконувались дії. |
| Фінанси для адміністратора | Поточна версія тільки про денну касу. Адміністратор бачить підсумкові фінанси за день, щоб підрахувати касу. | Admin finance visibility у v1 обмежується daily cash report. Довші фінансові періоди не входять у першу версію. |
| Owner-only дії | Власник керує типами абонементів і неробочими днями. Адміністратор може працювати з оплатами й заморозками, включно з виправленнями у межах поточного дня. | Permissions matrix стає простішою: owner-only для довідників, неробочих днів і службових речей; admin - для щоденної операційної роботи рецепції. |
| Хостинг і backup | Система буде на хостингу. Backup робить хостинг, а restore практично перевіряє власник. | Потрібна проста restore-check процедура для власника. Export даних з інтерфейсу у v1 не потрібен. |
| Втрата інтернету | Повертаються до паперу, а після появи інтернету все відмічають у системі. | Offline-first не потрібен. Потрібні прості ручні backdated entries з audit: хто вніс, коли фактично був візит/оплата, коли записали в систему. |
| Online payments | Малоймовірні, розмови про них немає. | Не проектувати payment platform у v1. Достатньо не закрити дорогу майбутньому payment method. |
| Client self-service | Ймовірно на цьому зупинимось. Якщо client self-service колись буде, бажано, щоб архітектурно його можна було додати без болючої перебудови. | Не робити у v1 і не обирати SPA/API тільки заради нього. Достатньо тримати доменну логіку окремо від admin UI, щоб майбутній read-only client view не був проблемою. |
| Barcode/turnstile | Малоймовірно. Поки достатньо цифр під штрихкодом; сканування може бути фічею з нижчим пріоритетом. | Card number лишається простим ідентифікатором. Barcode scanner можна додати пізніше як спосіб введення того самого номера, не як окрему архітектуру. |

Після цих відповідей shortlist стає впевненішим: modular monolith + hybrid server-rendered UI залишається найпрактичнішим шляхом. Найбільша доменна зона ризику тепер не міграція або інтеграції, а часті мінусові заняття і правильне пояснення, чим саме вони закриті.

## 3. Наявні класи рішень

BodyLife варто вчитися у gym/POS/membership систем, але не копіювати їхній масштаб.

| Клас рішення | Що в них зроблено добре | Що взяти для BodyLife | Що буде зайвим у v1 |
|---|---|---|---|
| Gym CRM / fitness club management | Профіль клієнта, member records, check-in, memberships, payments, reports, інколи access control. | Картка клієнта, статус абонемента, швидка відмітка візиту, історія оплат і відвідувань. | Member app, online booking, marketing, recurring billing, trainer payroll, inventory, turnstiles. |
| Membership management | Централізована база учасників, renewals/expirations, dues/payments, member activity. | Абонемент як бізнес-стан з датою, залишком занять, оплатою й попередженнями. | Public portal, events, email campaigns, subscriptions-first accounting. |
| Small business CRM | Customer directory, notes, history, segmentation, activity timeline. | Пошук по ПІБ/телефону/картці, короткий профіль, історія дій. | Lead pipeline, sales forecasting, marketing automation. |
| POS-light systems | Daily sales summary, payment methods, void/correction semantics, cashier visibility. | Денний звіт по готівці, список оплат, скасування або виправлення з історією. | Повний POS, склад, фіскалізація, чеки, товарні позиції. |
| Booking/attendance systems | Attendance, check-in flows, class booking, waitlists, no-show automation. | Факт візиту, список останніх візитів, клієнти, які давно не ходили. | Розклад груп, self-service booking, penalty charges, автоматичні нагадування клієнтам. |
| Open-source ERP/CRM/admin | Модульність, ролі, CRUD, reports, інколи бухгалтерія й POS. | Ідею модульних меж і адміністративних екранів. | ERP-ширина: accounting, HR, stock, orders, procurement, custom workflow engine. |

Головний урок: для BodyLife цінність не в кількості модулів, а в точній доменній моделі абонементів і швидкому reception workflow. Overengineering починається там, де система починає вирішувати задачі, яких бізнес ще не підтвердив.

## 4. Порівняння архітектурних підходів

Підходи відрізняються не модністю, а ціною змін і операційним тягарем.

| Підхід | Плюси | Мінуси | Прихована складність | Вплив на v1 | Fit |
|---|---|---|---|---|---|
| Layered architecture | Зрозуміла структура: UI, application, domain, data. | Може розмазати бізнес-правила по сервісах. | Потрібно не допустити дублювання формул у reports/UI. | Швидкий старт. | Середній |
| Feature/module-based architecture | Добре мапиться на Clients, Memberships, Visits, Payments, Reports. | Є ризик локальних копій спільних правил. | Потрібні правила залежностей між модулями. | Допоможе тримати v1 зрозумілою. | Високий |
| Modular monolith | Один deploy, але чіткі доменні межі. | Потребує дисципліни й ownership меж. | Легко назвати моноліт модульним, але не витримати межі. | Найкращий баланс для першої версії. | Високий |
| Service-oriented / microservices | Незалежні сервіси, масштабування, deployment boundaries. | Distributed transactions, latency, monitoring, deployment complexity. | Кожна бізнес-дія стає міжсервісною координацією. | Сповільнить v1 без реальної вигоди. | Низький |
| Event-driven | Добре для audit hooks, read models, notifications. | Ordering, retries, idempotency, eventual consistency. | Потрібно відрізняти domain events від event platform. | Корисно локально, не як окрема інфраструктура. | Умовний |
| Event sourcing | Повна історія змін і можливість відновлювати стан з events. | Projections, replay, schema evolution, складні виправлення. | Audit history не дорівнює event sourcing. | Занадто важко для v1. | Низький |
| Server-rendered web app | Простий mental model, менше frontend-state, швидкий CRUD. | Обмеженіша інтерактивність без JS-острівців. | Потрібно акуратно зробити швидкий пошук і дії рецепції. | Дуже практичний старт. | Високий |
| SPA + API | Багато інтерактивності, API може знадобитись mobile/client portal. | Два застосунки, auth/API versioning, більше JS-state. | Потрібно проектувати контракт і обробляти frontend consistency. | Може бути зайвим для v1. | Умовний |
| Hybrid SSR + interactive components | Прості сторінки плюс швидкі інтерактивні дії. | Потрібна дисципліна, щоб не створити хаос двох стилів. | Потрібно визначити, що інтерактивне, а що звичайна сторінка. | Дуже підходить для рецепції. | Високий |

## 5. Критика підходів

### Layered architecture

Добра як внутрішня структура, але погана як єдиний принцип. Формули абонемента, мінусів, скасувань, заморозок і звітів не повинні жити в контролерах або різних report-сервісах.

### Feature/module-based

Дає природну організацію навколо бізнесу. Ризик у тому, що модулі почнуть напряму лізти в дані один одного. Потрібні явні application interfaces.

### Modular monolith

Найкращий компроміс: один runtime, одна операційна модель, але чіткі доменні межі. Поганим стане, якщо межі будуть тільки в назвах папок.

### Microservices

Для малого залу, малої команди й v1 це передчасна ціна. Візит, оплата, абонемент і audit мають бути атомарними в головному workflow, а microservices ускладнять це без потреби.

### Event-driven

Корисний як внутрішній патерн: після `VisitRecorded` можна створити audit entry або оновити read model. Але брокер подій і distributed consumers для v1 не потрібні.

### Event sourcing

Спокушає повною історією, але вимагає projections, replay, versioning events і спеціального мислення. BodyLife потрібна бізнес-історія, не повний event-sourced core.

### Server-rendered app

Практична база для CRUD, reports, roles і простого деплою. Слабке місце - reception UX, тому точкові інтерактивні компоненти майже напевно знадобляться.

### SPA + API

Має сенс, якщо підтвердяться mobile app, client portal або зовнішні інтеграції. Інакше це зайвий frontend-state, API-contract і auth-surface для першої версії.

## 6. Ключові архітектурні теми

Найбільша складність не в CRUD, а в правилах і пояснюваності.

| Тема | Архітектурний висновок | Ризик, якщо зробити слабко |
|---|---|---|
| Модулі системи | Clients, Membership Types, Memberships, Visits, Payments, Freezes, Non-working Days, Reports, Audit, Users/Roles, Import/Export. | Усе стане одним великим admin CRUD без зрозумілих меж. |
| Межі відповідальності | Memberships володіє правилами активності, залишку, мінусів і продовжень. Reports читає готові правила, але не винаходить їх. | Різні екрани показуватимуть різний стан абонемента. |
| Бізнес-логіка абонементів | Потрібна окрема доменна модель для inclusive end date, counted visits, canceled visits, freezes, non-working overlap і negative visits. | Помилки у даті завершення, подвійні продовження, неправильний мінус. |
| Business audit/history | Append-only бізнес-історія: хто, коли, яку дію виконав, над яким об'єктом, з яким результатом або причиною. | Неможливо пояснити спірну оплату, скасування або заморозку. |
| Технічне логування | Окремо від audit: помилки, latency, auth failures, request id, background jobs, backup status. | Business audit загубиться в технічному шумі або технічні проблеми не будуть діагностовані. |
| Звіти | Daily report має бути read model або query layer поверх visits/payments, з однаковими правилами скасувань. | Денна готівка й відвідування не співпадуть з історією клієнтів. |
| Пошук клієнтів | Окремий search use case: card number, ПІБ, phone, last 4 digits, duplicate warnings. | Рецепція повернеться до паперу, якщо пошук повільний або нечіткий. |
| Backup/restore | Навіть якщо backup робить хостинг, у системі бажано мати мінімальний export і зрозумілу процедуру restore-check. | Відновлення даних залишиться припущенням, а не перевіреним процесом. |
| Міграція з Excel/паперу | У v1 не входить. Старт зі стерильної бази. Пізніше можна додати import або ручне заведення активних клієнтів з минулими датами. | Якщо модель не дозволить backdated membership, пізнє заведення активних клієнтів стане болючим. |
| Ролі користувачів | Owner і Admin у v1. Окремого Trainer доступу немає; довірений тренер користується девайсом адмін-стійки. | Передчасний RBAC ускладнить інтерфейс, але один спільний девайс вимагає акуратної audit visibility. |
| Майбутні інтеграції | Online payments і turnstile/barcode низької ймовірності. Client self-service можливий пізніше як read-only перегляд стану абонемента. | Не треба будувати API-first platform у v1, але домен не має бути зашитий тільки під admin screens. |

## 7. Реалістичний shortlist

### 1. Modular monolith + feature modules + hybrid server-rendered UI

Найсильніший кандидат. Дає один deploy і просту операційну модель, але не перетворює код на суцільний CRUD. Після уточнень він ще краще підходить: немає окремої trainer role, немає міграції у v1, online payments малоймовірні, offline-first не потрібен.

Може змінитися, якщо з'явиться кілька незалежних точок входу, юридично значима каса або зовнішній API раніше, ніж очікується.

### 2. Simple layered monolith + server-rendered UI

Найпростіший варіант для дуже малого обсягу. Швидко дає working version, але потребує обережності, щоб бізнес-правила абонементів не розмазались між UI, reports і data access.

Може бути достатнім, якщо v1 точно лишається маленькою внутрішньою системою. Але часті мінусові заняття й варіанти їх закриття вже підштовхують до чіткішого доменного ядра.

### 3. SPA + API + modular backend

Conditional option. Має сенс, якщо з'явиться близький план окремого клієнтського кабінету, зовнішніх інтеграцій або кількох різних frontend-клієнтів. Для v1 це все ще зайва складність.

Може стати першим вибором тільки після підтвердження близького API/client-facing майбутнього, якого зараз немає.

## 8. Що точно не варто робити у v1

Перша версія має бути нудно-надійною, а не великою платформою.

Не варто будувати:

- microservices або distributed workflow;
- full event sourcing;
- offline-first sync;
- повний ERP/POS/accounting;
- online payments і bank/terminal integrations;
- turnstiles, NFC, QR/barcode як основу v1;
- mobile app або public client portal у першій версії;
- trainer payroll і персональний облік тренерів;
- marketing automation і повідомлення клієнтам.

Не змішувати audit і logs:

- Business audit відповідає на питання "хто зробив бізнес-дію, коли, над чим і чому".
- Application logs відповідають на питання "що сталося технічно": помилка, latency, request id, auth failure, job status.
- Це різні потоки даних, різні читачі й різні правила зберігання.

## 9. Закриті питання та permissions

Після другого уточнення бізнес-межі v1 майже повністю визначені.

Закрито уточненнями:

- типи абонементів динамічні, не видаляються, можуть деактивуватися або виправлятися;
- дата завершення - останній активний день;
- ручне коригування дати завершення не потрібне;
- мінусові заняття часті й мають бути core workflow;
- міграція з Excel/паперу не входить у v1;
- окремого trainer access у v1 немає;
- адміністратор бачить тільки денну касу;
- export даних з інтерфейсу у v1 не потрібен;
- restore перевіряє власник;
- неробочі дні - owner-only;
- оплати й заморозки може виправляти адміністратор у межах поточного дня;
- online payments малоймовірні;
- barcode/turnstile низького пріоритету.

Лишилося оформити в ADR:

- точні поля audit для виправлення оплат, заморозок і backdated entries;
- чи існує поняття "закритий день" для каси, після якого виправлення має робити тільки власник;
- який мінімальний restore-check власник реально виконуватиме;
- як саме майбутній read-only client self-service має читати стан абонемента, якщо колись буде потрібен.

### Поточна матриця доступів для v1

| Дія | Роль | Чому так |
|---|---|---|
| Додавати, редагувати, деактивувати типи абонементів | Owner-only | Це змінює правила продажу, ціни, кількість занять і майбутні абонементи. Видалення краще заборонити. |
| Додавати клієнта, редагувати базові дані, прив'язувати картку | Admin + Owner | Це щоденна рецепційна робота. Потрібні duplicate warnings і audit, але не owner-only. |
| Видавати абонемент і фіксувати оплату | Admin + Owner | Адміністратор має працювати без очікування власника. Важливо логувати, хто видав і хто прийняв оплату. |
| Відмічати візит, у тому числі в мінус | Admin + Owner | Мінуси часті, тому блокування власником зламає reception flow. |
| Скасовувати помилковий візит | Admin + Owner | Це може бути оперативна помилка на рецепції. Потрібен коментар і audit. |
| Скасовувати або виправляти оплату | Admin + Owner у межах поточного дня | Це частина роботи адміністратора, якщо помилився з абонементом або оплатою. Потрібен audit: що було, що стало, хто і чому змінив. |
| Додавати заморозку | Admin + Owner | Заморозка є звичайною операцією по абонементу, але має бути видима в історії. |
| Скасовувати заморозку | Admin + Owner | Це операційна робота адміністратора: заморозити й розморозити абонемент. Потрібен коментар або причина в audit. |
| Додавати неробочий день або період | Owner-only | Це масово продовжує активні абонементи. Перед підтвердженням треба показувати кількість зачеплених абонементів. |
| Скасовувати неробочий день або відкатувати його вплив | Owner-only | Це небезпечна масова дія. Для v1 можна мінімізувати: показати вплив і дозволити ручне виправлення тільки через контрольований сценарій. |
| Бачити денний підсумок каси | Admin + Owner | Адміністратор має звірити касу за день. |
| Бачити фінансові звіти за довший період | Не входить у v1 | Поточна версія працює тільки з денною касою. Довші фінансові періоди можна залишити на майбутнє. |
| Export даних, backup/restore-панель, службові налаштування | Export не входить у v1 | Власник перевіряє restore, але окремий export з інтерфейсу зараз не потрібен. Службові налаштування лишаються owner-only, якщо з'являться. |

### Питання майбутнього розширення

Client self-service:

- найімовірніше, система зупиниться на внутрішньому admin/owner web app;
- client self-service не є планом v1 і не має диктувати вибір SPA/API;
- архітектурно достатньо тримати доменну логіку окремо від admin UI, щоб колись додати read-only перегляд без переписування ядра.

Пізнє заведення старих клієнтів:

- повний import не потрібен у v1;
- вручну створити клієнта з активним абонементом і минулою датою старту має бути можливо;
- такі записи варто маркувати як manually entered/backfilled в audit.

Після уточнень вибір може змінитися переважно через юридично значиму касу, нестабільний хостинг/інтернет, або якщо з'явиться вимога швидко переносити стару базу клієнтів з активними абонементами. Client self-service тепер не виглядає близьким драйвером, але його можна не блокувати архітектурно.

## 10. ADR-кандидати

Окремі рішення, які варто оформити перед реалізацією.

| ADR | Тема | Що має зафіксувати |
|---|---|---|
| ADR-001 | Product shape | Internal web app як основний формат v1, без mobile/desktop-first. |
| ADR-002 | Application architecture | Modular monolith vs simple layered monolith vs SPA/API. |
| ADR-003 | UI rendering | Server-rendered, SPA або hybrid interactive components. |
| ADR-004 | Module boundaries | Clients, Memberships, Visits, Payments, Extensions, Reports, Audit, Users. |
| ADR-005 | Membership invariants | Inclusive end date, remaining visits, frequent negative visits, first-negative-date rule, freeze/non-working overlap. |
| ADR-006 | Business audit | Окрема бізнес-історія дій, не application logs. |
| ADR-007 | Reporting | Daily cash/visits, inactive clients, low remaining visits, negative clients. |
| ADR-008 | Search | Card number, ПІБ, phone, last 4 digits, duplicate warnings. |
| ADR-009 | Backup/restore | Що покриває хостинг, як власник перевіряє restore, і чому export з інтерфейсу не входить у v1. |
| ADR-010 | Migration | У v1 без import; дозволити manual backfill активних клієнтів з audit, якщо знадобиться. |
| ADR-011 | Membership type lifecycle | Додавання, виправлення, деактивація без видалення; вплив змін на уже видані абонементи. |
| ADR-012 | Permissions matrix | Owner-only для типів абонементів і неробочих днів; admin daily cash; admin corrections для оплат/заморозок у межах поточного дня; no trainer role. |
| ADR-013 | Future client self-service boundary | Не реалізовувати у v1 і не робити API-first заради нього; тримати доменну логіку придатною для майбутнього read-only client view. |

## 11. Джерела

### Локальні документи BodyLife

- [first-version-requirements.md](first-version-requirements.md): детальні вимоги, сценарії, формули, звіти й open questions.
- [initial-context.txt](initial-context.txt): початковий контекст: простий internal web app, картки, готівка, базові сутності.
- [question-answering-interview.txt](question-answering-interview.txt): interview notes: папір/Excel, мінусові заняття, історія для спорів, ролі й пристрої.

### Зовнішні джерела і порівняльні системи

- [Mindbody](https://www.mindbodyonline.com/): fitness/wellness management: scheduling, check-in, payments, reporting.
- [GymMaster](https://www.gymmaster.com/): gym management: member records, memberships, bookings, sales, communication.
- [TeamUp](https://goteamup.com/product/gym-management-software/): gym software: memberships, bookings, payments, attendance limits, waitlists.
- [WildApricot Membership Management](https://www.wildapricot.com/features/membership-management-software): member database, payments, membership operations.
- [Odoo Membership / Partnership](https://www.odoo.com/documentation/19.0/applications/sales/crm/optimize/member_partner_module.html): membership sale, renewals, expirations, dues and payments.
- [Dolibarr ERP/CRM](https://www.dolibarr.org/): open-source modular ERP/CRM pattern: enable only needed features.
- [Square Customer Directory](https://squareup.com/us/en/point-of-sale/features/customer-directory): customer directory connected with purchase/payment history.
- [Square Reporting](https://squareup.com/help/us/en/article/5072-summaries-and-reports-from-the-online-dashboard): sales summaries, payment methods, transaction reports.
- [Martin Fowler: Microservices](https://martinfowler.com/articles/microservices.html): microservices as independently deployable services around business capabilities.
- [Martin Fowler: Microservice Trade-Offs](https://martinfowler.com/articles/microservice-trade-offs.html): microservices bring both benefits and costs; context matters.
- [Martin Fowler: Event Sourcing](https://martinfowler.com/eaaDev/EventSourcing.html): state changes captured as event objects for the lifetime of application state.
- [MDN: SPA](https://developer.mozilla.org/en-US/docs/Glossary/SPA): single-page application definition and client-side update model.
- [MDN: Progressive Web Apps](https://developer.mozilla.org/en-US/docs/Web/Progressive_web_apps): PWA/offline capabilities and service-worker-related APIs.
- [OWASP Logging Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html): guidance for application/security logging.
- [OWASP Top 10 A09](https://owasp.org/Top10/2021/A09_2021-Security_Logging_and_Monitoring_Failures/): high-value transactions should have integrity-protected audit trail.
- [NIST Audit Log Glossary](https://csrc.nist.gov/glossary/term/audit_log): audit log as chronological record and documentary evidence of events.

## Наступний крок

Документ є архітектурним shortlist, а не остаточним вибором технологій. Наступний крок - оформити ADR по архітектурі, permissions, audit, backup і membership rules.
