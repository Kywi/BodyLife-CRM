# ADR-006: Business audit, corrections and technical logs

## Статус

Accepted - 2026-07-07.

## Контекст

BodyLife CRM має пояснювати спірні візити, оплати, заморозки, скасування і продовження. Research прямо розділяє business audit і technical application logs: це різні читачі, різні питання і різні правила зберігання.

## Варіанти

- Append-only business audit table/stream.
- Object-specific history tables.
- Generic change log.
- Technical application logs only.
- Domain events reused directly as audit.
- Separate business audit plus separate technical logs.

## Рішення

BodyLife CRM v1 використовує separate append-only business audit і separate technical logs.

Business audit:

- є окремою бізнес-історією, не application logs;
- пишеться після успішних commands/workflows через in-process hooks/events;
- append-only: corrections не перезаписують минуле, а додають correction/cancellation entry;
- readable для owner і, в обмеженому scope, для admin.

Мінімальні поля audit:

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
- request/correlation id.

Audit обов'язковий для:

- payments;
- visits;
- freezes;
- non-working days;
- issue/cancel membership;
- backdated entries;
- corrections;
- membership type/settings changes.

Technical logs окремо покривають errors, latency, request id, auth failures, jobs, backup/restore status.

## Наслідки

- Owner отримує доказову бізнес-історію без технічного шуму.
- Debugging лишається можливим через technical logs.
- Corrections стають явними записами, а не silent edits.
- Потрібно проектувати audit schema як частину workflow, а не після реалізації.

## Що це означає для реалізації

- Додати окрему audit модель/table з append-only policy.
- Заборонити UPDATE/DELETE історичних audit entries через application workflows.
- У кожному state-changing command визначити audit action і domain summary.
- Для corrections/cancellations вимагати reason/comment там, де це бізнесово небезпечно.
- Не використовувати technical logs як єдине джерело бізнес-історії.
