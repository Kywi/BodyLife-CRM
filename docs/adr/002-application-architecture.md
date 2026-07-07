# ADR-002: Application architecture and application boundary

## Статус

Accepted - 2026-07-07.

## Контекст

Core workflow BodyLife CRM пов'язує клієнта, абонемент, візит, оплату, audit і денний звіт. Ці дані мають змінюватися узгоджено. Дослідження порівнювало layered architecture, feature/module-based architecture, modular monolith, SPA/API, microservices, event-driven patterns і event sourcing.

Для v1 важливі швидкість старту, low operational complexity і відсутність дублювання membership rules між UI, reports і data access.

## Варіанти

- Simple layered monolith.
- Feature/module-based monolith.
- Modular monolith.
- SPA + API + modular backend.
- Service-oriented або microservices.
- Local in-process event-driven patterns.
- Full event sourcing.

## Рішення

BodyLife CRM v1 будується як modular monolith:

- один застосунок;
- один deploy;
- одна основна transactional system;
- top-level структура навколо бізнес-модулів;
- внутрішні layers дозволені всередині модуля, але не є єдиним принципом архітектури.

Core workflow має бути транзакційно цілісним: візит, списання/мінус, оплата, audit і daily report не повинні роз'їжджатися.

Дозволені тільки local in-process events/hooks для audit, recalculation або lightweight read models. У v1 немає broker-based event infrastructure.

Явно відхиляємо для v1:

- microservices;
- distributed workflows;
- full event sourcing;
- offline-first sync;
- API-first SPA тільки заради гіпотетичного client portal.

## Наслідки

- Система має простий deploy і прості transaction boundaries.
- Бізнес-правила можна тримати в доменному/application ядрі, а не в UI.
- Майбутній read-only client view або integration лишаються можливими, якщо доменні queries не прив'язати до templates/controllers.
- Модульність потребує дисципліни: direct cross-module table writes і copied formulas мають бути заборонені.

## Що це означає для реалізації

- Організувати код за бізнес-модулями, а не тільки за `controllers/services/models`.
- Для кожного модуля визначити public commands/queries.
- Виконувати core state changes у server-side transactions.
- Додати in-process hooks/events після успішних commands для audit/read-model updates.
- Не створювати окремий public API як головний boundary v1, якщо немає реального споживача.
