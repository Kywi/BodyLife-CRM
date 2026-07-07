# ADR-005: Membership invariants and recalculation rules

## Статус

Accepted - 2026-07-07.

## Контекст

Абонементи - найризиковіша доменна зона v1. Потрібно коректно обробити inclusive end date, remaining visits, canceled visits, часті мінусові заняття, first negative date, freeze duration, non-working days і overlap rule: один календарний день не має автоматично давати подвійне продовження.

## Варіанти

- Store current computed values and recalculate on changes.
- Store events/facts and compute state on read.
- Hybrid: source facts plus derived state with controlled recalculation.
- Treat negative visits as membership state.
- Treat negative visits as separate debt-like record.
- Freeze/non-working extension as direct end-date mutation.
- Freeze/non-working extension as explicit source records.

## Рішення

BodyLife CRM v1 використовує hybrid model:

- зберігає source facts;
- Memberships централізовано рахує derived state.

Source facts:

- issued membership;
- visits;
- canceled visits;
- payments і one-off negative closures;
- freezes;
- non-working days;
- backdated entries;
- corrections.

Derived state:

- `active_status`;
- `remaining_visits`;
- `negative_balance`;
- `first_negative_visit_date`;
- `effective_end_date`;
- `extension_days`;
- warnings.

Правила:

- `end_date` inclusive: membership active if `today <= effective_end_date`.
- `effective_end_date` не редагується напряму.
- Зміна дати має source reason: freeze, non-working day, cancellation/correction або явний adjustment з audit.
- Мінуси є core workflow і рахуються як state абонемента з датою першого мінусового заняття.
- Закриття мінуса не приховується автоматично: новий абонемент стартує з `first_negative_visit_date` або мінус закривається one-off payments/visits.
- Freeze і non-working days дають extension source records.
- Перетин freeze/non-working рахується як union calendar days.

Відхиляємо для v1:

- pure event sourcing;
- direct manual editing of `end_date`;
- duplicated formulas in UI/reports;
- окремий debt ledger для мінусів, доки достатньо membership state + explicit closure workflow.

## Наслідки

- Стан абонемента пояснюваний через source facts.
- Corrections і cancellations можуть безпечно тригерити recalculation.
- Мінуси стають нормальним workflow, а не exception.
- Доменні правила треба тестувати незалежно від UI.

## Що це означає для реалізації

- Створити domain/application service для membership recalculation.
- Усі commands, які змінюють visits/payments/freezes/non-working days/backfill, мають тригерити recalculation.
- Додати domain tests для inclusive end date, cancellation, negative visits, first negative date, freeze/non-working overlap і backdated entries.
- У client profile і reports показувати membership state тільки через Memberships queries.
- Audit записувати і source fact, і correction/recalculation reason там, де це потрібно для пояснення.
