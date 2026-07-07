# ADR-003: UI rendering and interaction model

## Статус

Accepted - 2026-07-07.

## Контекст

Рецепція потребує швидкого пошуку, швидкого відкриття клієнта, видимого статусу абонемента, попереджень, відмітки візиту, оплат і денного звіту. Generic CRUD screens не достатні для цього workflow.

Research shortlist включав mostly server-rendered UI, SPA + API і hybrid server-rendered UI з інтерактивними компонентами.

## Варіанти

- Mostly server-rendered UI.
- SPA + API.
- Hybrid server-rendered UI з інтерактивними компонентами.
- Minimal admin CRUD без спеціального reception flow.

## Рішення

BodyLife CRM v1 використовує hybrid server-rendered UI.

Сервер рендерить:

- базові сторінки;
- форми;
- client profile;
- reports;
- settings/admin screens.

Інтерактивність додається тільки там, де вона прискорює рецепцію:

- quick/live search;
- compact client results;
- membership status panel;
- warnings;
- quick actions: record visit, add payment, issue membership, add freeze;
- duplicate-submit protection і loading states.

Перший екран - reception dashboard, не generic CRUD.

Усі state-changing дії йдуть через server-side commands/actions. Після дії UI перечитує canonical state із сервера.

## Наслідки

- UI лишається простішим за full SPA, але отримує потрібну швидкість для рецепції.
- Frontend state не стає джерелом бізнес-правил.
- Потрібно чітко визначити interactive islands, щоб hybrid model не перетворився на хаос.
- Tablet-first reception UX стає acceptance criterion, а не прикрасою.

## Що це означає для реалізації

- Починати з reception dashboard як головного workflow v1.
- Не робити full SPA/API як default.
- Для state-changing buttons додати disabled/loading state, idempotency або duplicate-submit guard.
- Для destructive/correction actions додати confirmation і reason/comment, якщо це вимагають ADR-006/ADR-012.
- Тестувати reception slice на tablet і phone widths.
