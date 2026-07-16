# Accepted ADR package for BodyLife CRM

Дата початкового пакета: 2026-07-07

Оновлено: 2026-07-16 (ADR-016)

Цей пакет фіксує ADR-кандидати з `docs/architecture-research-options.md` і `docs/adr/adr-backlog.md` як прийняті архітектурні рішення для першої web-версії BodyLife CRM.

## Статус пакета

Усі ADR у цьому пакеті мають статус `Accepted`.

Це означає:

- рішення достатньо визначене, щоб на нього спиратися під час реалізації v1;
- зміна рішення потребує нового ADR або явного оновлення наявного ADR;
- backlog лишається дослідницьким контекстом, а цей каталог є робочим джерелом прийнятих рішень.

## ADR

| ADR | Рішення |
|---|---|
| [ADR-001](001-product-shape-and-operating-model.md) | Internal hosted web app для одного залу, без offline-first, mobile app, client portal і multi-tenant SaaS у v1. |
| [ADR-002](002-application-architecture.md) | Modular monolith: один deploy, бізнес-модулі, транзакційно цілісний core workflow. |
| [ADR-003](003-ui-rendering-and-interaction-model.md) | Hybrid server-rendered UI з інтерактивністю тільки для reception-critical зон. |
| [ADR-004](004-module-boundaries-and-business-rule-ownership.md) | Top-level business modules, Memberships володіє правилами абонементів. |
| [ADR-005](005-membership-invariants-and-recalculation.md) | Source facts плюс централізований derived state для абонементів, мінусів і продовжень. |
| [ADR-006](006-business-audit-corrections-and-technical-logs.md) | Append-only business audit окремо від technical logs. |
| [ADR-007](007-reporting-model-and-consistency-rules.md) | Reports як query/report layer поверх canonical records і Memberships queries. |
| [ADR-008](008-search-identity-card-rules-and-duplicate-warnings.md) | Clients/Search володіє пошуком, card rules, phone normalization і duplicate warnings. |
| [ADR-009](009-backup-restore-and-operational-recovery.md) | Hosting/provider-managed backups плюс обов'язковий restore-check перед production use. |
| [ADR-010](010-migration-manual-backfill-and-paper-fallback.md) | Без full import у v1; manual backfill і paper fallback entries через domain commands та audit. |
| [ADR-011](011-membership-type-lifecycle.md) | Editable MembershipType catalog плюс immutable snapshot у виданому абонементі. |
| [ADR-012](012-permissions-session-accountability-and-corrections.md) | Owner, named Admin і shared Reception/Admin account з чіткою accountability. |
| [ADR-013](013-future-client-self-service-boundary.md) | Client self-service не входить у v1; домен лишається придатним для майбутнього read-only view. |
| [ADR-014](014-visit-membership-selection-and-freeze-policy.md) | Multiple Memberships дозволені, але Visit завжди має explicit selection/context; frozen Membership не споживається. |
| [ADR-015](015-freeze-range-eligibility-policy.md) | Freeze починається тільки в locked pre-command effective date interval lifecycle-active Membership і не може перекривати active counted Membership Visit. |
| [ADR-016](016-non-working-day-application-scope.md) | NonWorkingDay застосовується snapshot-ом до lifecycle-active Memberships з inclusive overlap і додає кожному весь підтверджений period. |

## Джерела

- `docs/architecture-research-options.md`
- `docs/adr/adr-backlog.md`
- `docs/first-version-requirements.md`
- `docs/initial-context.txt`
- `docs/question-answering-interview.txt`

## Рекомендований порядок реалізації

1. Зафіксувати skeleton modular monolith і top-level modules: ADR-001, ADR-002, ADR-004.
2. Реалізувати домен абонементів і типів абонементів: ADR-005, ADR-011.
3. Додати audit, actor/session model і permissions: ADR-006, ADR-012.
4. Перед Visits зафіксувати explicit membership/context selection і Freeze eligibility: ADR-014.
5. Перед Freezes зафіксувати range eligibility і Visit-conflict policy: ADR-015.
6. Перед NonWorkingDays зафіксувати affected-scope і full-period policy: ADR-016.
7. Зібрати reception vertical slice: ADR-003, ADR-008.
8. Підтвердити consistency reports після visits/payments/corrections: ADR-007.
9. Закрити production readiness: ADR-009, ADR-010.
10. Тримати майбутній client self-service як guardrail, а не scope v1: ADR-013.
