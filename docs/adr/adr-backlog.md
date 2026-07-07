# ADR backlog для BodyLife CRM

Дата: 2026-07-06  
Статус: backlog архітектурних рішень перед реалізацією  
Основа: `docs/architecture-research-options.md`, `docs/first-version-requirements.md`, `docs/initial-context.txt`, `docs/question-answering-interview.txt`, `graphify query` по наявному `graphify-out/graph.json`

## Призначення

Цей документ перетворює наявне архітектурне дослідження BodyLife CRM на backlog ADR. Він не обирає стек, мову, фреймворк, базу даних або хостинг. Мета - показати, які архітектурні рішення треба явно прийняти перед або під час старту реалізації першої версії.

Горизонт рішень: перша робоча web-версія для внутрішнього обліку залу, де головна цінність - швидка рецепція, правильний облік абонементів, готівкових оплат, мінусів, заморозок, неробочих днів, історії дій і денного звіту.

## Рішення, які блокують початок реалізації

Ці ADR треба прийняти до побудови основної структури застосунку і доменної моделі:

- ADR-001: Product shape і операційна модель v1.
- ADR-002: Application architecture і межі застосунку.
- ADR-004: Module boundaries і ownership бізнес-правил.
- ADR-005: Membership invariants і правила перерахунку.
- ADR-006: Business audit, corrections і відокремлення від technical logs.
- ADR-008: Search identity, card/phone rules і duplicate warnings.
- ADR-011: Membership type lifecycle.
- ADR-012: Permissions matrix, session accountability і межі виправлень.

## Рішення, які можна відкласти

Ці ADR не повинні затримувати старт, якщо для v1 зафіксовано мінімальні припущення:

- ADR-009: Backup/restore interface можна відкласти, але restore-check procedure треба мати перед production use.
- ADR-010: Full migration/import можна відкласти, якщо manual backfill явно дозволений і audit маркує такі записи.
- ADR-013: Future client self-service boundary можна відкласти, якщо доменна логіка не зашивається тільки в admin UI.
- Розширені financial reports за місяць, barcode/turnstile, online payments, mobile app, notifications і trainer payroll не входять у ADR backlog v1, доки не зміняться бізнес-обмеження.

## Рішення для vertical slice prototype

Ці рішення краще перевірити через малий наскрізний прототип, бо вони мають UX, data integrity і audit ризики:

- ADR-003: UI rendering and interaction model для reception workflow.
- ADR-005: Membership invariants на сценаріях мінусів, freeze/non-working overlap і cancellation.
- ADR-006: Business audit plus correction flow для візиту, оплати, заморозки і backdated entry.
- ADR-007: Reporting consistency між daily cash report, visits/payments і client history.
- ADR-008: Search experience на картці, ПІБ, телефоні, останніх 4 цифрах і duplicate warnings.

## ADR-001. Product Shape І Операційна Модель V1

Контекст:
Перша версія має замінити паперовий і частково Excel-облік абонементів. Дослідження вже розглядає найбільш імовірну форму продукту як внутрішній web app, а не mobile app чи повну SaaS-платформу. На рецепції очікується робочий планшет або телефон, у власника - доступ зі свого телефону. Інтернет на рецепції вважається стабільним, а при втраті інтернету бізнес повертається до паперу і потім вносить записи в систему.

Які варіанти треба порівняти:
- Internal hosted web app для owner/admin.
- Local/LAN або desktop-first system.
- PWA/offline-first web app.
- Mobile-first app або окремий native app.
- SaaS-like platform with multi-tenant assumptions.

Критерії рішення:
- Швидкість роботи адміністратора на рецепції.
- Простота доступу власника з телефону.
- Низька операційна складність для малого залу.
- Поведінка при втраті інтернету.
- Простота backup/restore і підтримки.
- Чи не тягне варіант зайві v1-модулі: mobile app, online payments, turnstile, complex accounting.

Ризики:
- Offline-first може суттєво ускладнити модель синхронізації без підтвердженої потреби.
- Desktop/LAN може ускладнити доступ власника і backup.
- SaaS-like assumptions можуть створити зайві ролі, tenants і billing-concepts.
- Hosted web app без ручного fallback-процесу може зламати операції при інтернет-збої.

Питання треба уточнити:
- Який саме пристрій буде основним на рецепції: планшет, телефон, ноутбук.
- Який мінімальний fallback-процес на папері прийнятний при втраті інтернету.
- Чи потрібна окрема production/staging процедура для власника.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли зафіксовано product shape v1, target devices, internet-loss behavior, excluded surfaces, operational ownership і мінімальні acceptance criteria для старту production use.

Decision: це внутрішній hosted web app для одного залу, для owner/admin workflows. Основний пристрій — планшет або телефон на рецепції, власник може заходити зі свого телефону. Offline-first, mobile app, client portal і SaaS/multi-tenant модель не входять у v1. При втраті інтернету бізнес тимчасово повертається до паперу, а записи потім вносяться в систему з audit


## ADR-002. Application Architecture І Межі Застосунку

Контекст:
Research порівнює layered architecture, feature/module-based architecture, modular monolith, service-oriented/microservices, event-driven, event sourcing, server-rendered UI і SPA/API. Для BodyLife важливі атомарні бізнес-дії: візит, абонемент, оплата, audit і денний звіт мають узгоджено відображати один стан.

Які варіанти треба порівняти:
- Simple layered monolith.
- Feature/module-based monolith.
- Modular monolith.
- SPA + API + modular backend.
- Service-oriented або microservices.
- Local event-driven patterns without external event infrastructure.
- Full event sourcing.

Критерії рішення:
- Чи легко тримати membership rules в одному місці.
- Чи підтримує архітектура швидкий v1 без enterprise-overengineering.
- Чи не ускладнює атомарність візиту, оплати, абонемента і audit.
- Чи є зрозумілі module boundaries і dependency rules.
- Чи можна пізніше додати read-only client view або integration без переписування ядра.
- Операційна простота одного deploy.

Ризики:
- Layered-only structure може розмазати бізнес-правила між UI, reports і data access.
- "Modular" моноліт може лишитися тільки назвами папок без реальних меж.
- SPA/API може додати зайвий frontend state і API-versioning для v1.
- Microservices або event sourcing можуть зробити малий workflow непропорційно складним.

Питання треба уточнити:
- Наскільки близько очікується external API або client self-service.
- Який рівень модульних меж потрібен перед першою реалізацією.
- Які бізнес-дії мають бути атомарними.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли обрано application architecture class, описано module/dependency rules, transactional boundaries для core workflow і explicitly rejected підходи для v1.

Decision:
BodyLife CRM v1 будується як modular monolith: один застосунок, один deploy, одна основна транзакційна система, але код організований навколо бізнес-модулів, а не як “все в контролерах”. Layered structure можна використовувати всередині модулів, але не як єдиний архітектурний принцип.
Ключові пункти рішення:
Memberships володіє правилами активності, залишку занять, мінусів, заморозок і продовжень. UI, reports і data access не дублюють ці формули.
Core workflow має бути транзакційно цілісний: візит, списання/мінус, оплата, audit і денний звіт не повинні роз’їжджатися.
Модулі v1: Clients, MembershipTypes, Memberships, Visits, Payments, Freezes, NonWorkingDays, Reports, Audit, Users/Roles.
Reports читає канонічні дані або read model, але не має власної бізнес-логіки абонементів.
Дозволені тільки локальні in-process events/hooks, наприклад для audit або read model. Без брокера подій у v1.
Явно відхиляємо для v1: microservices, distributed workflow, full event sourcing, offline-first sync, API-first SPA заради гіпотетичного client portal.

## ADR-003. UI Rendering And Interaction Model

Контекст:
Рецепція потребує швидкого пошуку, швидкого відкриття клієнта, видимого статусу абонемента, попереджень, відмітки візиту, оплати і денного звіту. Research shortlists server-rendered UI, SPA + API і hybrid SSR + interactive components.

Які варіанти треба порівняти:
- Mostly server-rendered UI.
- SPA + API.
- Hybrid server-rendered UI з інтерактивними компонентами для пошуку, швидких дій і попереджень.
- Minimal admin CRUD screens without special reception flow.

Критерії рішення:
- Час до знаходження клієнта і відмітки візиту.
- Ясність стану абонемента: дата, залишок, мінус, freeze, non-working extension.
- Стійкість до double-submit або accidental duplicate actions.
- Простота підтримки і тестування UI state.
- Чи достатньо UX для планшета/телефону на рецепції.

Ризики:
- Pure server-rendered UI може бути незручним для живого пошуку і швидких попереджень.
- SPA може додати складний client state без підтвердженої потреби.
- Hybrid approach може стати хаотичною сумішшю стилів без меж.
- CRUD-first UI може повернути адміністратора до паперу через повільний workflow.

Питання треба уточнити:
- Які дії мають бути доступні з першого reception screen.
- Чи треба auto-open exact card-number match.
- Який мінімальний responsive layout для телефону власника і планшета адміністратора.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли описано UI rendering style, інтерактивні зони, allowed page transitions, double-action safeguards і критерії успішного reception flow.

Desicion:
BodyLife CRM v1 використовує hybrid server-rendered UI: базові сторінки, форми, профіль клієнта, звіти й налаштування рендеряться сервером, а інтерактивність додається тільки там, де вона реально пришвидшує рецепцію: пошук, швидкі дії, попередження і захист від повторних кліків.
Ключові пункти рішення:
Перший екран — не generic CRUD, а reception dashboard: пошук клієнта, номер картки, ПІБ/телефон, останні візити, короткі попередження, перехід до денного звіту.
Інтерактивні зони: live/quick search, compact client result, membership status panel, warnings, кнопки Відмітити візит, Додати оплату, Видати абонемент, Додати заморозку.
Exact unique card-number match можна відкривати швидко після submit/scan; якщо збіг неунікальний або неповний, показувати список результатів.
Усі state-changing дії йдуть через server-side commands/actions, після чого UI перечитує канонічний стан із сервера. Бізнес-правила не живуть у frontend state.
Для visit/payment/freeze/cancel потрібні safeguards: disabled/loading submit, idempotency або duplicate-submit guard, confirmation для скасувань і масових дій.
Layout: tablet-first для рецепції, phone-friendly для власника; desktop теж працює, але не диктує дизайн.
Явно відхиляємо для v1: full SPA + API як основний UI, pure server-rendered UI без швидкого пошуку/feedback, і minimal admin CRUD без спеціального reception flow.

## ADR-004. Module Boundaries І Ownership Бізнес-Правил

Контекст:
Research виділяє модулі Clients, Membership Types, Memberships, Visits, Payments, Freezes, Non-working Days, Reports, Audit, Users/Roles, Import/Export. Найважливіше - не дозволити reports або UI винаходити власні правила активності, залишку, мінусів і продовжень.

Які варіанти треба порівняти:
- Modules by business capability: Clients, Memberships, Visits, Payments, Reports, Audit, Users.
- Layers by technical concern: UI, application, domain, data.
- Hybrid: business modules with internal layers.
- CRUD resource modules without explicit domain ownership.

Критерії рішення:
- Хто володіє rules for active membership, remaining visits, negative balance, freeze/non-working extension.
- Як Reports читає доменний стан без дублювання формул.
- Як Audit отримує бізнес-події без змішування з technical logs.
- Як модулі спілкуються: direct calls, application services, domain events, query/read models.
- Як уникати циклічних залежностей.

Ризики:
- Різні екрани покажуть різний стан абонемента.
- Reports почнуть мати власні формули, які розходяться з профілем клієнта.
- Payments і Visits не матимуть спільного correction model.
- Audit стане післядумкою, а не частиною workflow.

Питання треба уточнити:
- Які module interfaces потрібні вже у v1.
- Чи є окремий module для Extensions або це частина Memberships.
- Чи daily report має бути query layer, read model або звичайний report service.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли зафіксовано список v1-модулів, ownership правил, dependency direction, shared concepts і заборонені cross-module shortcuts.

Decision:
BodyLife CRM v1 організовується навколо бізнес-модулів, а не навколо технічних шарів або CRUD-ресурсів. Усередині кожного модуля можна мати internal layers, але top-level ownership визначається бізнес-відповідальністю.

V1 modules:
- Clients/Search: профіль клієнта, картка, телефон, duplicate warnings і пошук.
- MembershipTypes: довідник типів абонементів, owner-only lifecycle, no-delete/deactivation.
- Memberships: канонічний стан абонемента і всі правила active status, remaining visits, negative balance, first negative date, freeze/non-working overlap, effective end date.
- Visits: факти візитів і скасування візитів.
- Payments: готівкові оплати, прив'язка до абонемента, виправлення оплат.
- Freezes: source records для заморозок.
- NonWorkingDays: owner-only source records для неробочих днів і масового впливу.
- Reports: query/report layer поверх канонічних records/read models, без власних формул абонемента.
- Audit: append-only business audit, який отримує бізнес-події з commands/workflows.
- Users/Roles: owner/admin permissions і actor/session accountability.

Ownership rules:
Memberships володіє всіма формулами абонемента. UI, Reports, Visits, Payments, Freezes і NonWorkingDays не рахують самостійно активність, залишок, мінуси або продовження. Вони використовують public commands/queries Memberships.

Freezes і NonWorkingDays володіють причинами/джерелами продовження, але не приховано редагують end date. Memberships обчислює фактичну дату завершення з урахуванням overlap rules.

Окремий Extensions module у v1 не вводиться. Extension є частиною membership state/source facts. Якщо пізніше з'являться ручні продовження як окремий workflow, це можна винести в окремий module.

Dependency rules:
Модулі спілкуються через application services/public interfaces. Заборонені direct cross-module table writes і дублювання business formulas. Shared concepts обмежуються IDs/value objects: ClientId, MembershipId, Money, DateRange, ActorId.

Reports у v1 є звичайним query/report service або lightweight read model, але джерелом істини лишаються Visits, Payments і Memberships. Daily report не має власної membership logic.

Audit отримує business events/hooks in-process після успішних commands. У v1 немає event broker, distributed events або event sourcing.

Forbidden shortcuts:
- business rules in templates/controllers/frontend state;
- reports with copied membership formulas;
- direct editing of effective end date without source reason;
- technical logs as replacement for business audit;
- cross-module database writes outside owned workflows.

## ADR-005. Membership Invariants І Правила Перерахунку

Контекст:
Це найризиковіша доменна зона. Потрібно зафіксувати inclusive end date, remaining visits, canceled visits, frequent negative visits, first negative date, freeze duration, non-working days і overlap rule: один календарний день не має автоматично давати подвійне продовження.

Які варіанти треба порівняти:
- Store current computed values and recalculate on changes.
- Store events/facts and compute state on read.
- Hybrid: store source facts plus derived state with controlled recalculation.
- Treat negative visits as membership state.
- Treat negative visits as separate debt-like record.
- Freeze/non-working extension as direct end-date mutation.
- Freeze/non-working extension as explicit extension records.

Критерії рішення:
- Пояснюваність: чому саме така дата завершення і залишок.
- Коректність після cancellation або correction.
- Підтримка мінусів як частого core workflow.
- Відсутність подвійних продовжень при overlap.
- Можливість backdated entries після паперового fallback.
- Простота тестування правил без UI.

Ризики:
- Direct editing of end date може приховати причини змін.
- Derived fields можуть застаріти, якщо recalculation не централізований.
- Event-only model може бути занадто складним для v1.
- Неправильна модель мінусів зламає старт нового абонемента і daily explanations.

Питання треба уточнити:
- Чи потрібні manual extensions окрім freeze і non-working days.
- Як саме фіксувати закриття мінусів: новим абонементом, разовими оплатами або обома сценаріями.
- Чи дозволяти backdated visit/payment/membership і хто може це робити.
- Які test cases є обов'язковими для прийняття правил.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли записано invariants, source facts, derived values, recalculation triggers, correction behavior, negative closure rules і мінімальний набір domain examples/tests.

Decision:
BodyLife CRM v1 зберігає незмінні/кориговані source facts: виданий абонемент, візити, скасування, оплати/разові закриття, заморозки, неробочі дні, backdated entries і corrections. Модуль Memberships є єдиним власником перерахунку derived state: active_status, remaining_visits, negative_balance, first_negative_visit_date, effective_end_date, extension_days і попереджень.
Ключові правила:
end_date inclusive: абонемент активний, якщо today <= effective_end_date.
effective_end_date не редагується напряму; зміна дати має мати source reason: freeze, non-working day, cancellation/correction або інший явний adjustment з audit.
Мінуси є core workflow, а не edge case: вони рахуються як стан абонемента з датою першого мінусового заняття.
Закриття мінуса не приховується автоматично: або новий абонемент стартує з first_negative_visit_date, або мінус закривається разовими оплатами/візитами.
Freeze і non-working days не мутують дату напряму; вони дають extension source records. Перетин рахується як union календарних днів, тобто один день не продовжує абонемент двічі.
Будь-яке скасування/виправлення візиту, заморозки, неробочого дня, backdated membership/payment тригерить централізований recalculation і business audit.
Відхилити для v1: чистий event-sourcing, пряме ручне редагування end_date, дублювання формул у UI/reports, і окремий “debt ledger” для мінусів, поки достатньо стану абонемента + явного сценарію закриття.

## ADR-006. Business Audit, Corrections І Technical Logs

Контекст:
History and transparency matter because disputed visits, payments, freezes, cancellations and extensions must be explainable. Research прямо застерігає не змішувати business audit з ordinary application logs.

Які варіанти треба порівняти:
- Append-only business audit table/stream for key business actions.
- Object-specific history tables.
- Generic change log/audit trail.
- Technical application logs only.
- Domain events reused for audit.
- Separate business audit plus separate technical logs.

Критерії рішення:
- Чи можна відповісти "хто, коли, що зробив, над чим і чому".
- Чи видно before/after для corrections.
- Чи видно фактичну дату бізнес-події і дату внесення в систему.
- Чи audit захищений від тихого редагування.
- Чи technical logs лишаються придатними для debugging.
- Чи audit читається власником без технічного шуму.

Ризики:
- Generic technical logs не дадуть власнику доказової історії.
- Object-specific histories можуть дублювати формат і пропускати cross-object workflow.
- Domain events можуть не містити user-facing explanation.
- Надто важкий audit може сповільнити v1 і ускладнити corrections.

Питання треба уточнити:
- Точні поля audit для payments, visits, freezes, non-working days і backdated entries.
- Чи обов'язковий comment/reason для cancellation або correction.
- Чи існує "закритий день" каси, після якого correction owner-only.
- Який retention і visibility потрібні для audit.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли зафіксовано audit scope, event/action schema, actor/session model, correction semantics, relation to technical logs і read access для owner/admin.
Desicion:
separate append-only business audit + separate technical logs.
Суть рішення:
Business audit є окремою бізнес-історією, не application logs.
Audit пишеться після успішних commands/workflows через in-process hooks/events.
Audit append-only: виправлення не перезаписують минуле, а додають новий correction/cancellation entry.
Мінімальні поля: actor, role, session/device, action_type, entity_type/id, related ids, occurred_at, recorded_at, before/after або domain summary, reason/comment, request/correlation_id.
Обов’язково audit для оплат, візитів, заморозок, неробочих днів, видачі/скасування абонемента, backdated entries, corrections, membership type/settings changes.
Technical logs окремо: errors, latency, request id, auth failures, background jobs, backup status; не є джерелом бізнес-правди.
Owner бачить повну бізнес-історію; admin бачить історію, потрібну для рецепції й поточного дня. Technical logs не є owner/admin UI.
Відхилити: “technical logs only”, generic changelog як єдиний audit, object-specific history без cross-workflow audit, full event sourcing для v1.

## ADR-007. Reporting Model І Consistency Rules

Контекст:
V1 reports include daily cash/visits, memberships ending soon, low remaining visits, negative clients and inactive clients. Daily report має узгоджуватися з visits/payments і враховувати cancellations/corrections.

Які варіанти треба порівняти:
- Reports as direct queries over source records.
- Reports as maintained read models.
- Reports as exported snapshots.
- Daily cash report with open/closed day lifecycle.
- No closed day, always live recalculation.

Критерії рішення:
- Daily cash sum must match payment history after corrections.
- Canceled visits must not count as visits.
- Owner/admin can understand why a number appears.
- Reports are fast enough for daily use.
- Reports reuse membership rules instead of duplicating logic.
- Thresholds can be fixed for v1 without blocking future configurability.

Ризики:
- Live reports may change after corrections without clear explanation.
- Snapshots may drift from source records.
- Read models add operational complexity if not needed.
- No day closure policy can create disputes around cash reconciliation.

Питання треба уточнити:
- Чи потрібне поняття "закритого дня" для денного звіту.
- Хто може виправляти оплату після поточного дня.
- Чи daily cash report має показувати correction history.
- Які thresholds для inactive clients і low remaining visits є v1 defaults.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли визначено report source of truth, correction behavior, day lifecycle if any, included v1 reports, thresholds і consistency checks між report і history.

Decision:
BodyLife CRM v1 використовує Reports як query/report layer поверх канонічних source records: Visits, Payments, Memberships і Audit. Reports не мають власних формул активності абонемента, залишку занять, мінусів або дат завершення; ці значення читаються з Memberships/public queries.

Для v1 основний підхід — live direct queries over source records. Maintained read models дозволені тільки як оптимізація, якщо звіти стануть повільними, і мають перебудовуватись із source records. Exported snapshots не є source of truth.

Daily report рахується за вибрану дату:
- visits count = зараховані, не скасовані візити за дату;
- payments count = не скасовані/чинні оплати за дату;
- cash sum = сума чинних готівкових оплат за дату;
- кожен підсумок має drill-down список записів, з яких він складений.

Корекції й скасування не переписують історію тихо. Вони створюють correction/cancellation records, оновлюють live totals і видимі в audit/history. Якщо корекція зроблена після закриття/звірки дня, daily report має показати, що звіт змінився після close/reconciliation.

Day lifecycle для v1 мінімальний: поточний день open; після звірки/закриття cash day корекції дозволені тільки за правилами permissions і завжди з audit reason. Закриття дня не заморожує “правду”, а фіксує reconciliation point.

V1 reports:
- daily cash/visits report;
- memberships ending soon: `days_left <= 7`;
- low remaining visits: `remaining_visits <= 2`;
- negative clients;
- inactive clients with thresholds `14 / 30 / 60 days` plus manual selection if cheap.

Reject for v1:
- report formulas copied into UI/controllers;
- exported snapshots as canonical reports;
- heavy maintained read models before there is a performance need;
- monthly/advanced financial reporting.

## ADR-008. Search Identity, Card Rules І Duplicate Warnings

Контекст:
Пошук на рецепції - один з головних драйверів v1. Потрібні card number, ПІБ, phone, last 4 digits, duplicate warnings. Barcode/QR/NFC/turnstile не входять у v1, але card number лишається основним ідентифікатором.

Які варіанти треба порівняти:
- Exact card-number first search.
- Full-text-like name/phone search.
- Structured search by fields.
- Fuzzy matching for duplicates.
- Unique active card number constraint.
- Allow multiple historical card assignments.

Критерії рішення:
- Швидкість знаходження клієнта на рецепції.
- Відсутність небезпечних дублювань card number.
- Зручність пошуку без картки.
- Попередження при схожому ПІБ, phone або existing card.
- Підтримка клієнтів без картки.
- Майбутня сумісність зі scanner as input method.

Ризики:
- Надто слабкі duplicate warnings створять подвійних клієнтів.
- Надто жорстка унікальність може заблокувати виправлення помилок.
- Fuzzy search може показувати забагато шуму на рецепції.
- Scanner-specific assumptions можуть ускладнити v1 без користі.

Питання треба уточнити:
- Чи card number може змінюватися або перевидаватися.
- Чи телефон є обов'язковим.
- Які поля достатні для duplicate warning при створенні клієнта.
- Що робити з duplicate client у v1: тільки warning чи merge scenario.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли визначено searchable identifiers, uniqueness rules, duplicate warnings, no-card scenario, scanner boundary і expected reception search behavior.

Decision:
BodyLife CRM v1 використовує Clients/Search як власника пошуку клієнтів, номерів карток, нормалізації телефонів і duplicate warnings.

Searchable identifiers:
- current card number;
- прізвище, ім'я, по батькові та повне ПІБ;
- normalized phone;
- last 4 digits of phone.

Card rules:
- клієнт може існувати без номера картки;
- current card number, якщо заповнений, має бути унікальним серед поточних прив'язок;
- одна картка не може бути прив'язана до двох клієнтів одночасно;
- card number можна змінити або перевидати, але зміна має йти через явну дію з audit;
- історичні прив'язки карток дозволені, але тільки одна current assignment активна для конкретного номера.

Search behavior:
- exact card-number match має пріоритет;
- якщо exact card-number match єдиний, можна швидко відкрити compact client/profile після submit/scan;
- якщо збігів кілька або запит неповний, показувати список результатів;
- пошук без картки має працювати по ПІБ, телефону і останніх 4 цифрах телефону.

Duplicate warnings:
- при створенні або редагуванні клієнта система попереджає про existing card number, existing normalized phone і схожий ПІБ;
- duplicate phone або схожий ПІБ не блокують створення, але вимагають явного підтвердження;
- duplicate current card number блокується;
- merge clients не входить у v1, для v1 достатньо warning + ручного виправлення.

Scanner boundary:
- barcode scanner у майбутньому вважається лише способом введення того самого card number;
- QR/NFC/turnstile і scanner-specific identity не входять у v1.

## ADR-009. Backup, Restore І Operational Recovery

Контекст:
Research фіксує, що система буде на хостингу, backup робить хостинг, restore практично перевіряє власник, а export даних з інтерфейсу у v1 не потрібен. Водночас втрата даних є системним ризиком для CRM, яка замінює папір.

Які варіанти треба порівняти:
- Hosting-provider managed backups only.
- Managed backups plus owner-visible restore-check procedure.
- App-level export for owner.
- Manual database dump managed by developer/operator.
- Periodic restore rehearsal in staging.

Критерії рішення:
- Відновлення реально перевірене, а не припущене.
- Власник розуміє мінімальний restore-check.
- Немає зайвого export UI, якщо він не потрібен v1.
- Backup frequency і retention відповідають бізнес-ціні втрати дня.
- Restore procedure does not require heroic developer-only knowledge.

Ризики:
- Provider backup без restore rehearsal може не спрацювати у критичний момент.
- App-level export може створити privacy/security ризики і зайвий scope.
- Developer-only backups залишають бізнес залежним від однієї людини.
- Backdated paper fallback без audit може створити розрив після outage.

Питання треба уточнити:
- Який максимальний прийнятний data loss window: день, кілька годин, менше.
- Який restore-check власник реально готовий виконувати.
- Чи потрібен read-only export пізніше для спокою власника.
- Хто відповідає за incident communication і recovery.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли зафіксовано backup owner, restore owner, frequency/retention expectation, restore-check steps, paper fallback reconciliation і причину відкладення app-level export.

Decision:
BodyLife CRM v1 використовує hosting/provider-managed backups як основний backup-механізм, але production-ready вважається тільки після перевіреного restore-check.

Прийняти:
- managed automated backups + документований restore runbook;
- periodic restore rehearsal у staging/test environment;
- owner-visible restore-check checklist після відновлення;
- paper fallback reconciliation через backdated entries з audit;
- technical logs/status для backup/restore jobs.

Не включати у v1:
- app-level export UI для власника;
- backup/restore панель в інтерфейсі;
- developer-only manual dumps як основний механізм backup.

Очікування для v1:
- backup owner: hosting/provider + technical operator/developer;
- restore owner: technical operator/developer;
- restore acceptance: owner перевіряє checklist;
- retention: мінімум 30 днів автоматичних backup;
- RPO: прагнути до кількох годин/PITR, але не гірше 24 годин;
- RTO: same-business-day restore для production incident;
- перед production use обов’язково виконати хоча б один restore rehearsal.

## ADR-010. Migration, Manual Backfill І Paper Fallback Entries

Контекст:
Research після уточнень каже: v1 стартує зі стерильною базою, full import не входить у v1. Але модель не повинна заважати ручному створенню клієнта з уже активним абонементом і датою старту в минулому. При втраті інтернету записи можуть спочатку вестися на папері, а потім заноситися в систему.

Які варіанти треба порівняти:
- No migration and no backfill.
- Manual backfill for active clients only.
- Full import from Excel/paper.
- Import later as separate tool.
- Backdated entries with audit markers.

Критерії рішення:
- Чи можна почати v1 без довгої підготовки даних.
- Чи не блокується внесення активного абонемента з минулою датою.
- Чи видно, що запис внесено заднім числом.
- Чи не виникає фальшива історія, ніби дія сталася в день внесення.
- Чи можна пізніше додати import без перебудови домену.

Ризики:
- No backfill може змусити бізнес вести старі активні абонементи на папері довше.
- Full import може з'їсти v1 scope і принести брудні дані.
- Backdated entries без audit зіпсують довіру до history.
- Import later може виявити, що модель не підтримує історичні дати.

Питання треба уточнити:
- Скільки активних клієнтів реально треба завести на старті.
- Чи потрібен migration day або система запускається поступово.
- Які поля мінімальні для manual backfill.
- Хто має право створювати backfilled membership.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли визначено migration scope for v1, backfill permissions, required historical fields, audit marking і future import boundary.

Decision:
BodyLife CRM v1 не включає повний import/migration з Excel або паперу. Система стартує зі стерильною базою.

Прийняти:
- manual backfill тільки для активних клієнтів/абонементів, якщо це потрібно на старті або під час поступового переходу;
- paper fallback після втрати інтернету: записи ведуться на папері, а потім вносяться як backdated visits/payments/freezes/memberships;
- усі backfilled/backdated записи проходять через звичайні domain commands, validation, recalculation і business audit;
- audit обов’язково показує `occurred_at` / фактичну дату бізнес-події, `recorded_at` / дату внесення в систему, actor, reason/comment і marker: `manual_backfill` або `paper_fallback`;
- для активного абонемента без повної історії дозволити explicit opening state як source fact: дата старту, тип/снапшот абонемента, поточний залишок занять або мінус, відома дата завершення/extension state, причина й джерело даних.

Не включати у v1:
- full Excel/paper import;
- migration day як обов’язковий процес;
- direct database edits;
- фальшиву генерацію старої історії без реальних записів;
- unmarked backdated entries.

Future boundary:
Повний import можна додати пізніше як окремий tool/workflow через staging + validation + audit, але не як прямий запис у production tables.

## ADR-011. Membership Type Lifecycle

Контекст:
Типи абонементів динамічні, можуть додаватися, не видаляються, редагування потрібне тільки для виправлення помилок. Owner керує типами абонементів. Важливо вирішити, як зміни типу впливають або не впливають на вже видані абонементи.

Які варіанти треба порівняти:
- Editable membership type referenced by issued memberships.
- Immutable snapshot copied into issued membership.
- Versioned membership type.
- Deactivation instead of deletion.
- Owner-only edit with audit.

Критерії рішення:
- Вже виданий абонемент не змінюється непомітно через edit довідника.
- Виправлення помилки можливе без небезпечного delete.
- Owner can manage future sales rules.
- Reports can explain historical price/type.
- UI лишається простим для малого залу.

Ризики:
- Mutable reference може змінити історію старих абонементів.
- Snapshot-only може ускладнити масове виправлення помилки.
- Versioning може бути зайво складним для v1.
- Delete може зламати історичні records.

Питання треба уточнити:
- Які поля типу можуть редагуватися після створення.
- Чи ціна історичного абонемента має фіксуватися на момент видачі.
- Чи потрібна причина для деактивації або виправлення.
- Як показувати неактивні типи в admin UI.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли зафіксовано lifecycle: create, edit, deactivate, no-delete policy, issued-membership snapshot/reference behavior, permissions і audit.

Decision:
BodyLife CRM v1 використовує editable MembershipType catalog + immutable issued-membership snapshot.

MembershipType:
- поля: name, duration_days, visits_limit, price, is_active;
- створення, редагування й деактивація тільки Owner;
- delete заборонений;
- inactive типи не доступні для нових звичайних продажів, але лишаються видимими в історії.

Issued Membership:
- зберігає `membership_type_id` як reference;
- також копіює snapshot на момент видачі: type_name, duration_days, visits_limit, price;
- подальші зміни MembershipType не змінюють уже видані абонементи;
- reports/history читають історичну ціну й правила зі snapshot, а не з поточного довідника.

Audit:
- audit обов’язковий для create/edit/deactivate MembershipType;
- audit має before/after, actor, reason/comment, recorded_at;
- якщо треба виправити вже виданий абонемент через помилку в типі, це окремий explicit correction workflow з audit, не автоматичне масове оновлення.

Відхилити для v1:
- hard delete MembershipType;
- mutable reference як єдине джерело правил для виданих абонементів;
- повне versioning/history table для кожної зміни типу, якщо snapshot + audit достатньо.

## ADR-012. Permissions Matrix, Session Accountability І Межі Виправлень

Контекст:
V1 має owner і admin. Окремого trainer role немає; довірений тренер може користуватись адмінським девайсом/доступом, якщо адміна немає. Owner-only: типи абонементів, неробочі дні і службові речі. Admin може працювати з оплатами, візитами, заморозками і виправленнями у межах поточного дня.

Які варіанти треба порівняти:
- Two roles: Owner/Admin.
- Owner/Admin plus simplified trusted-staff role.
- Shared admin device with named login.
- Shared admin device without strong actor identity.
- Current-day admin corrections plus owner-only after day close.
- No day close, all corrections by role.

Критерії рішення:
- Audit must show actor for disputed actions.
- Admin workflow should not wait for owner for daily operations.
- Dangerous mass actions should be owner-only.
- Permissions must remain understandable.
- Trainer substitute scenario should not break accountability.
- Cash corrections need clear boundary.

Ризики:
- Shared login без actor clarity знизить цінність audit.
- Надто складний RBAC сповільнить v1.
- Admin без correction limits може змінювати стару касу без контролю.
- Owner-only для щоденних дій зламає reception workflow.

Питання треба уточнити:
- Чи кожен адмін має персональний login.
- Як довірений тренер входить у систему, якщо замінює адміна.
- Чи існує close-of-day action.
- Які corrections потребують comment/reason і owner approval.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли матриця доступів покриває v1 actions, dangerous actions, correction windows, actor/session model, trainer-substitute scenario і audit expectations.

Decision update:
BodyLife CRM v1 використовує Owner account, named Admin account і shared Reception/Admin account.

Accounts:
- Owner: персональний акаунт власника.
- Named Admin: персональний акаунт основного адміністратора.
- Shared Reception/Admin: спільний акаунт для робочого девайса, тренера або заміни адміністратора.

Правило роботи:
- якщо основний адмін працює сам, він використовує свій named Admin account;
- якщо адмін іде зі зміни або пристрій лишається для іншої людини, він виходить зі свого акаунта;
- тренер/заміна/будь-хто на рецепції входить у shared Reception/Admin account;
- не дозволяти працювати “за адміна” під його персональним акаунтом.

Audit:
- для named Admin audit означає: дію зробив або відповідає конкретний адмін;
- для shared Reception/Admin audit означає: дію зроблено зі shared reception session;
- audit завжди фіксує account_id, account_type, role, device/session, action, occurred_at, recorded_at, before/after або summary, reason/comment;
- система не вдає, що знає фізичну людину за shared account.

## ADR-013. Future Client Self-Service Boundary

Контекст:
Client self-service не входить у v1 і, ймовірно, може взагалі не знадобитися. Але research каже не закривати дорогу майбутньому read-only client view: доменна логіка не має жити тільки в admin UI.

Які варіанти треба порівняти:
- Ignore client self-service entirely in v1.
- Keep domain/application logic independent from admin UI.
- API-first architecture from day one.
- Later read-only client portal as separate interface.
- Public client account model in v1.

Критерії рішення:
- V1 не ускладнюється заради непідтвердженого майбутнього.
- Майбутній read-only view може отримати коректний membership state.
- Sensitive data і permissions не змішані з admin actions.
- Domain rules reusable outside admin screens.
- Architecture does not force SPA/API only for speculative needs.

Ризики:
- API-first заради майбутнього portal може бути зайвим scope.
- Повне ігнорування може зашити правила в templates/controllers.
- Public account assumptions можуть додати auth/privacy проблеми.
- Майбутній portal може вимагати privacy boundary, не продуманий у v1.

Питання треба уточнити:
- Чи є реальний timeline для client self-service.
- Які дані клієнт міг би бачити read-only.
- Чи потрібна окрема client identity/auth model, якщо колись буде portal.
- Які admin-only поля ніколи не мають виходити назовні.

Що буде вважатися прийнятим рішенням:
ADR прийнятий, коли зафіксовано, що саме не робиться у v1, які domain/application boundaries треба зберегти, і які future-facing assumptions заборонено тягнути в першу версію.

Decision:
BodyLife CRM v1 залишається internal Owner/Admin web app. Client self-service, client accounts, public portal і mobile/client-facing API не входять у v1.

Прийняти:
- не будувати client portal у v1;
- не робити API-first architecture тільки заради гіпотетичного майбутнього порталу;
- не створювати client identity/auth model у v1;
- не змішувати admin permissions із майбутніми client permissions;
- membership calculations, remaining visits, effective_end_date, negative balance і warnings мають жити в domain/application layer, а не в templates/controllers;
- admin UI читає стан абонемента через application queries/public read methods, які теоретично можна буде використати для майбутнього read-only client view.

Future boundary:
Якщо client self-service колись з’явиться, це буде окремий read-only interface поверх окремих client-safe queries/DTO, а не відкриття admin screens назовні.

Client-safe дані потенційно:
- статус абонемента;
- залишок занять;
- дата завершення;
- мінус, якщо є;
- базова історія відвідувань/оплат тільки після окремого privacy decision.

Не виносити назовні без нового ADR:
- audit log;
- admin comments/notes;
- cash reports;
- correction history;
- internal warnings;
- чужі дані або пошук клієнтів.

Відхилити для v1:
- public client accounts;
- self check-in;
- online payments;
- API-first platform;
- SPA вибір лише через можливий майбутній portal.

## Рекомендований порядок опрацювання

1. ADR-001, ADR-002, ADR-004 - зафіксувати форму системи, архітектурний стиль і модульні межі.
2. ADR-005, ADR-011 - зафіксувати доменну модель абонементів і типів абонементів.
3. ADR-006, ADR-012 - зафіксувати audit, actor/session model, corrections і permissions.
4. ADR-003, ADR-008 - перевірити reception workflow через vertical slice.
5. ADR-007 - перевірити consistency daily report після visits/payments/corrections.
6. ADR-009, ADR-010 - закрити production-readiness: restore-check, backfill і paper fallback reconciliation.
7. ADR-013 - оформити як guardrail, щоб не обирати API-first або SPA тільки заради гіпотетичного client portal.

## Мінімальний vertical slice для перевірки ADR

Прототип має покрити один наскрізний сценарій:

```text
створити клієнта
-> знайти за карткою / ПІБ / телефоном
-> видати абонемент
-> зафіксувати оплату
-> відмітити візити до 0 і в мінус
-> закрити мінус новим абонементом або разовою оплатою
-> додати заморозку
-> додати неробочий день з overlap
-> скасувати помилковий візит або оплату
-> побачити client history, audit і daily report
```

Вертикальний зріз не має деталізувати код або остаточний стек. Його мета - довести, що обрані рішення можуть одночасно підтримати швидкий reception UX, правильні membership calculations, пояснювану історію і узгоджений денний звіт.
