# ADR-010: Migration, manual backfill and paper fallback entries

## Статус

Accepted - 2026-07-07.

## Контекст

Research після уточнень фіксує: v1 стартує зі стерильною базою, full import з Excel/паперу не входить у v1. Але система має дозволити ручне заведення активного клієнта з минулою датою старту, якщо це потрібно. При втраті інтернету записи ведуться на папері, а потім вносяться в систему.

## Варіанти

- No migration and no backfill.
- Manual backfill for active clients only.
- Full import from Excel/paper.
- Import later as separate tool.
- Backdated entries with audit markers.

## Рішення

BodyLife CRM v1 не включає full import/migration з Excel або паперу. Система стартує зі стерильною базою.

Приймаємо:

- manual backfill тільки для активних клієнтів/абонементів, якщо потрібно;
- paper fallback після втрати інтернету;
- backdated visits/payments/freezes/memberships через звичайні domain commands;
- validation, recalculation і business audit для всіх backfilled/backdated records;
- явне розділення фактичної дати бізнес-події і дати внесення в систему.

Audit для backfill/fallback має містити:

- `occurred_at`;
- `recorded_at`;
- actor/account;
- reason/comment;
- marker: `manual_backfill` або `paper_fallback`.

Для активного абонемента без повної історії дозволений explicit opening state:

- дата старту;
- membership type або snapshot;
- поточний залишок занять або мінус;
- відома дата завершення/extension state;
- причина і джерело даних.

Не включаємо у v1:

- full Excel/paper import;
- mandatory migration day;
- direct database edits;
- synthetic fake history;
- unmarked backdated entries.

## Наслідки

- V1 може стартувати швидше, без великого migration project.
- Активні клієнти можуть бути заведені вручну без ламання домену.
- Історія лишається чесною: видно, що внесено заднім числом.
- Повний import можна додати пізніше окремим workflow/tool.

## Що це означає для реалізації

- Commands для visits/payments/freezes/memberships мають підтримувати `occurred_at`.
- Audit має завжди фіксувати `recorded_at` окремо від `occurred_at`.
- Opening state має бути валідним source fact, а не прямим database patch.
- Future import, якщо з'явиться, має йти через staging, validation, commands і audit.
- UI має показувати backfilled/fallback nature там, де це важливо для history.
