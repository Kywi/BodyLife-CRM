# План роботи після прийнятих ADR

Дата: 2026-07-07

Цей документ описує флоу після того, як ADR для першої web-версії BodyLife CRM прийняті. Його мета - перетворити архітектурні рішення на конкретні артефакти для реалізації: baseline, domain model, data design, interaction contracts, operations plan, stack decision, vertical slice і roadmap.

## Поточний етап

Зараз проєкт знаходиться тут:

```text
бізнес-вимоги
-> архітектурне дослідження
-> accepted ADR package
-> post-ADR implementation planning  <- зараз
-> technical design artifacts
-> vertical slice
-> реалізація v1
```

Уже є:

- `docs/first-version-requirements.md` - бізнес-вимоги v1.
- `docs/architecture-research-options.md` - архітектурне дослідження і shortlist.
- `docs/adr/` - прийнятий ADR package.

Далі не потрібно знову "досліджувати архітектуру з нуля". Далі треба виробити implementation contract: які саме артефакти мають бути перед кодом і в якому порядку.

## Принципи флоу

- Не починати з повної реалізації всіх таблиць або всього UI.
- Не порушувати accepted ADR без нового ADR.
- Спочатку деталізувати домен, потім дані, потім взаємодію, потім стек і vertical slice.
- Усі state-changing дії мають проходити через server-side commands/actions.
- Membership rules належать модулю `Memberships`, а не UI, reports або frontend state.
- Business audit є окремою бізнес-історією, не technical logs.
- Перший proof of architecture - vertical slice, а не повний продукт.

## Загальний порядок

| Фаза | Назва | Результат |
|---|---|---|
| 1 | Architecture baseline | Один документ, який стискає ADR у реалізаційні правила |
| 2 | Domain model | Сутності, інваріанти, lifecycle, edge cases |
| 3 | Data architecture | Schema direction, constraints, indexes, audit tables, migration/backfill |
| 4 | Interaction contracts | Commands, queries, transactions, permissions, errors |
| 5 | UI workflow specification | Reception dashboard, profile, warnings, quick actions |
| 6 | Operations design | Logs, business audit, backup/restore, fallback, recovery |
| 7 | Technology stack decision | Мова, фреймворк, БД, hosting, migrations, test approach |
| 8 | Vertical slice plan | Малий наскрізний сценарій для перевірки архітектури |
| 9 | Implementation roadmap | Milestones, порядок задач, quality gates |

## Фаза 1. Architecture baseline

### Мета

Перетворити 13 ADR на короткий implementation contract. Це документ, до якого можна повертатися під час реалізації, щоб перевірити: "ми не порушуємо архітектуру?".

### Очікуваний документ

`docs/architecture-baseline.md`

### Має містити

- product shape;
- application architecture;
- top-level modules;
- dependency rules;
- state-changing workflow rules;
- UI rendering rules;
- audit/logging rules;
- reporting rules;
- backup/fallback rules;
- explicitly rejected approaches for v1.

### Prompt

```text
Використай accepted ADR package у docs/adr/.

Сформуй docs/architecture-baseline.md для BodyLife CRM v1.

Мета документа - не повторити ADR повністю, а перетворити їх на короткий implementation contract для розробки.

Включи:
- product shape;
- application architecture;
- module boundaries;
- dependency rules;
- ownership бізнес-правил;
- UI rendering model;
- state-changing command rules;
- business audit vs technical logs;
- reporting consistency rules;
- backup/restore і paper fallback rules;
- що явно не входить у v1.

Формат:
1. Короткий висновок
2. Non-negotiable architecture rules
3. Module map
4. Allowed dependencies
5. Forbidden shortcuts
6. Implementation implications
7. Quality gates before coding

Не обирай стек у цьому документі.
Не додавай нових архітектурних рішень, якщо вони не випливають з ADR.
```

### Done criteria

- Документ відповідає всім ADR.
- Немає нових рішень без посилання на ADR.
- Є чіткий список "можна / не можна" для реалізації.

## Фаза 2. Domain model

### Мета

Формально описати домен до бази даних і коду. Найважливіше - абонементи, мінуси, заморозки, неробочі дні, скасування і перерахунок.

### Очікуваний документ

`docs/domain-model.md`

### Має містити

- business entities;
- lifecycle кожної сутності;
- invariants;
- state transitions;
- calculation rules;
- edge cases;
- domain test scenarios.

### Prompt

```text
Використай $design-data-architecture.

На основі docs/first-version-requirements.md і accepted ADR package у docs/adr/ сформуй docs/domain-model.md.

Не починай з таблиць.
Спочатку опиши домен BodyLife CRM як бізнес-модель.

Обов'язково покрий:
- Client;
- card number;
- MembershipType;
- issued Membership;
- Visit;
- Payment;
- Freeze;
- NonWorkingDay;
- Reports;
- Audit;
- User/Actor.

Для Memberships окремо опиши:
- inclusive end date;
- remaining visits;
- negative balance;
- first negative visit date;
- new membership after negative visits;
- freeze extension;
- non-working day extension;
- overlap freeze + non-working day;
- cancellation/recalculation;
- backdated entries.

Формат:
1. Domain overview
2. Entities
3. Invariants
4. Lifecycles
5. Calculation rules
6. Correction and cancellation rules
7. Edge case matrix
8. Domain test scenarios
9. Open implementation questions

Не обирай БД і не створюй schema design у цьому документі.
```

### Done criteria

- Кожне важливе правило з ADR-005 має доменний сценарій.
- Є edge case matrix для мінусів, скасувань, заморозок і неробочих днів.
- Можна написати domain tests без UI.

## Фаза 3. Data architecture

### Мета

Перетворити domain model у data model: таблиці/структури, constraints, indexes, audit records, migration/backfill.

### Очікуваний документ

`docs/data-architecture.md`

### Prompt

```text
Використай $design-data-architecture.

На основі docs/domain-model.md і docs/adr/ сформуй docs/data-architecture.md.

Мета - спроєктувати data model для modular monolith web app.

Покрий:
- entities/tables;
- relationships;
- constraints;
- indexes для пошуку;
- derived state vs source facts;
- recalculation strategy;
- audit tables;
- correction/cancellation representation;
- manual backfill/opening state;
- paper fallback entries;
- migration strategy;
- backup/restore implications.

Формат:
1. Data architecture summary
2. Source facts
3. Derived state
4. Proposed schema outline
5. Constraints and indexes
6. Audit data model
7. Reporting data access
8. Backfill/fallback model
9. Migration and backup implications
10. Risks and validation scenarios

Не прив'язуйся до конкретного ORM, але познач, які можливості БД потрібні.
```

### Done criteria

- Є схема, яку можна перенести в migrations.
- Кожен report має зрозуміле джерело даних.
- Audit і technical logs не змішані.

## Фаза 4. Interaction contracts

### Мета

Описати commands/queries до UI і коду. Це майбутні application services.

### Очікуваний документ

`docs/interaction-contracts.md`

### Prompt

```text
Використай $design-system-interactions.

На основі docs/domain-model.md, docs/data-architecture.md і docs/adr/ сформуй docs/interaction-contracts.md.

Опиши server-side commands/actions і queries для BodyLife CRM v1.

Обов'язково включи:
- CreateClient;
- UpdateClient;
- AssignOrChangeCard;
- CreateMembershipType;
- Edit/DeactivateMembershipType;
- IssueMembership;
- MarkVisit;
- CancelVisit;
- CreatePayment;
- CorrectPayment;
- AddFreeze;
- CancelFreeze;
- AddNonWorkingDay;
- CorrectNonWorkingDay;
- GenerateDailyReport;
- SearchClients.

Для кожної command опиши:
- purpose;
- input;
- validation;
- permissions;
- transaction boundary;
- affected modules;
- recalculation;
- audit event;
- possible errors;
- UI result.

Для queries опиши:
- input;
- output shape;
- source modules;
- consistency expectations.

Не створюй implementation code.
```

### Done criteria

- Кожна state-changing дія має permission, transaction boundary і audit event.
- Reports не дублюють membership formulas.
- UI може будуватися поверх цих contracts.

## Фаза 5. UI workflow specification

### Мета

Спроєктувати не красиві екрани, а робочі workflows рецепції.

### Очікуваний документ

`docs/ui-workflows.md`

### Prompt

```text
На основі docs/interaction-contracts.md і docs/adr/003-ui-rendering-and-interaction-model.md сформуй docs/ui-workflows.md.

Мета - описати UI workflows для hybrid server-rendered web app.

Покрий:
- reception dashboard;
- client search;
- exact card match;
- multiple search results;
- client profile;
- active membership panel;
- warnings;
- mark visit flow;
- issue membership flow;
- add payment flow;
- add/cancel freeze flow;
- daily report flow;
- correction flows;
- owner/admin differences.

Для кожного workflow дай:
- user goal;
- screen/state;
- primary actions;
- warnings;
- confirmations;
- loading/duplicate-submit protection;
- success state;
- failure state.

Не створюй дизайн-макети.
Не додавай features поза v1.
```

### Done criteria

- Reception flow можна пройти без читання бізнес-вимог.
- Є tablet-first і phone-friendly очікування.
- Немає generic CRUD як основного UX.

## Фаза 6. Operations design

### Мета

Зробити систему готовою до реального використання: audit, logs, backup, restore, fallback, support.

### Очікуваний документ

`docs/operations-design.md`

### Prompt

```text
Використай $design-observability-operations.

На основі docs/adr/006-business-audit-corrections-and-technical-logs.md, docs/adr/009-backup-restore-and-operational-recovery.md, docs/adr/010-migration-manual-backfill-and-paper-fallback.md і docs/interaction-contracts.md сформуй docs/operations-design.md.

Покрий:
- business audit policy;
- technical logging policy;
- metrics;
- backup strategy;
- restore rehearsal;
- paper fallback;
- reconciliation after outage;
- support workflow;
- production readiness checklist.

Формат:
1. Operational goals
2. Business audit
3. Technical logs
4. Backup/restore
5. Paper fallback and backdated entries
6. Support and correction workflow
7. Production readiness checklist
8. Risks

Не обирай vendor/tool, якщо deployment stack ще не обраний.
```

### Done criteria

- Є restore-check procedure.
- Є правила paper fallback.
- Є список audit events для commands.

## Фаза 7. Technology stack decision

### Мета

Тепер, коли зрозумілі domain, data, interactions і operations, можна обрати стек.

### Очікуваний документ

`docs/technology-stack-decision.md`

### Prompt

```text
Використай $choose-technology-stack.

На основі docs/architecture-baseline.md, docs/domain-model.md, docs/data-architecture.md, docs/interaction-contracts.md, docs/operations-design.md і docs/adr/ сформуй docs/technology-stack-decision.md.

Порівняй стеки для реалізації BodyLife CRM v1.

Обов'язково оцінюй через прийняті ADR:
- hosted internal web app;
- modular monolith;
- hybrid server-rendered UI;
- server-side commands/actions;
- relational consistency;
- business audit;
- reports;
- backup/restore;
- paper fallback;
- low operational complexity.

Порівняй:
- backend language/framework;
- UI approach;
- database;
- ORM/migrations;
- hosting;
- logging/error handling;
- testing approach.

Формат:
1. Decision drivers
2. Options matrix
3. Pros/cons/risks
4. Recommended stack or shortlist
5. What would change the decision
6. Migration/backup implications
7. Implementation starter plan

Якщо бракує даних, не роби фінальний вибір - дай shortlist і validation plan.
```

### Done criteria

- Стек не суперечить ADR.
- Є зрозуміле пояснення, чому він підходить саме цьому домену.
- Відомо, як робити migrations, tests, backup і deployment.

## Фаза 8. Vertical slice plan

### Мета

Перевірити архітектуру маленьким наскрізним шматком до повної реалізації.

### Очікуваний документ

`docs/vertical-slice-plan.md`

### Recommended slice

```text
login
-> reception dashboard
-> search client
-> client profile
-> active membership state
-> mark visit
-> recalculation
-> business audit event
-> daily report update
```

### Prompt

```text
На основі docs/architecture-baseline.md, docs/domain-model.md, docs/data-architecture.md, docs/interaction-contracts.md, docs/ui-workflows.md, docs/operations-design.md і docs/technology-stack-decision.md сформуй docs/vertical-slice-plan.md.

Мета - описати перший vertical slice, який перевіряє архітектуру BodyLife CRM v1.

Покрий:
- user story;
- involved modules;
- UI screens;
- commands/queries;
- data records;
- audit events;
- recalculation;
- report consistency;
- tests;
- success criteria;
- what is intentionally excluded from the slice.

Формат:
1. Slice goal
2. User scenario
3. Scope
4. Out of scope
5. Technical flow
6. Test plan
7. Acceptance criteria
8. Risks
```

### Done criteria

- Slice перевіряє membership rules, audit і report consistency.
- Slice не роздувається до всієї системи.
- Після slice можна приймати рішення про повну реалізацію.

## Фаза 9. Implementation roadmap

### Мета

Перетворити technical design на порядок реалізації.

### Очікуваний документ

`docs/implementation-roadmap.md`

### Prompt

```text
На основі всіх post-ADR документів сформуй docs/implementation-roadmap.md.

Мета - зробити покроковий roadmap реалізації BodyLife CRM v1 після vertical slice.

Розбий на milestones:
1. Project scaffold and infrastructure
2. Auth/users/roles
3. Clients and search
4. Membership types
5. Memberships and recalculation
6. Visits and cancellations
7. Payments and corrections
8. Freezes and non-working days
9. Reports
10. Business audit/history UI
11. Backup/restore/paper fallback readiness
12. Production hardening

Для кожного milestone дай:
- ціль;
- залежності;
- задачі;
- acceptance criteria;
- потрібні тести;
- ризики;
- що не входить.

Не пиши код.
```

### Done criteria

- Roadmap можна переносити в issue tracker.
- Кожен milestone має acceptance criteria.
- Є залежності між milestones.

## Контрольний checklist перед стартом коду

Код можна починати, коли існують:

- `docs/architecture-baseline.md`;
- `docs/domain-model.md`;
- `docs/data-architecture.md`;
- `docs/interaction-contracts.md`;
- `docs/ui-workflows.md`;
- `docs/operations-design.md`;
- `docs/technology-stack-decision.md`;
- `docs/vertical-slice-plan.md`;

і для них виконано:

- немає суперечностей з ADR;
- є acceptance criteria;
- є test scenarios;
- зрозуміло, що входить і не входить у v1;
- vertical slice не роздутий до повної системи;
- backup/restore не лишений "на потім" без плану.

## Найближчий наступний prompt

Починати треба з architecture baseline:

```text
Використай accepted ADR package у docs/adr/.

Сформуй docs/architecture-baseline.md для BodyLife CRM v1.

Мета документа - не повторити ADR повністю, а перетворити їх на короткий implementation contract для розробки.

Включи:
- product shape;
- application architecture;
- module boundaries;
- dependency rules;
- ownership бізнес-правил;
- UI rendering model;
- state-changing command rules;
- business audit vs technical logs;
- reporting consistency rules;
- backup/restore і paper fallback rules;
- що явно не входить у v1.

Формат:
1. Короткий висновок
2. Non-negotiable architecture rules
3. Module map
4. Allowed dependencies
5. Forbidden shortcuts
6. Implementation implications
7. Quality gates before coding

Не обирай стек у цьому документі.
Не додавай нових архітектурних рішень, якщо вони не випливають з ADR.
```

