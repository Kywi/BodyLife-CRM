# ADR-011: Membership type lifecycle

## Статус

Accepted - 2026-07-07.

## Контекст

Типи абонементів динамічні: можуть додаватися, не видаляються, редагування потрібне для виправлення помилок. Owner керує типами абонементів. Критично вирішити, чи зміна типу впливає на вже видані абонементи.

## Варіанти

- Editable membership type referenced by issued memberships.
- Immutable snapshot copied into issued membership.
- Versioned membership type.
- Deactivation instead of deletion.
- Owner-only edit with audit.

## Рішення

BodyLife CRM v1 використовує editable MembershipType catalog плюс immutable issued-membership snapshot.

MembershipType:

- поля: `name`, `duration_days`, `visits_limit`, `price`, `is_active`;
- create/edit/deactivate тільки Owner;
- hard delete заборонений;
- inactive типи не доступні для нових звичайних продажів;
- inactive типи лишаються видимими в history/reports.

Issued Membership:

- зберігає `membership_type_id` reference;
- копіює snapshot на момент видачі: `type_name`, `duration_days`, `visits_limit`, `price`;
- подальші зміни MembershipType не змінюють уже видані абонементи;
- reports/history читають історичну ціну і правила зі snapshot.

Audit:

- обов'язковий для create/edit/deactivate MembershipType;
- містить before/after, actor, reason/comment, recorded_at;
- виправлення вже виданого абонемента через помилку в типі є окремим explicit correction workflow.

Відхиляємо для v1:

- hard delete MembershipType;
- mutable reference як єдине джерело правил виданих абонементів;
- повне versioning/history table для кожної зміни типу, якщо snapshot + audit достатньо.

## Наслідки

- Історичні продажі не змінюються непомітно після редагування довідника.
- Owner може керувати майбутніми правилами продажу.
- Деактивація безпечніша за delete.
- Якщо треба змінити вже виданий абонемент, це явна correction, а не side effect.

## Що це означає для реалізації

- Додати snapshot fields у issued membership.
- На issue membership копіювати values з MembershipType.
- Заборонити delete на application рівні і, бажано, на persistence policy рівні.
- Фільтрувати inactive types зі звичайного issue-membership flow.
- Додати owner-only authorization і audit для змін MembershipType.
