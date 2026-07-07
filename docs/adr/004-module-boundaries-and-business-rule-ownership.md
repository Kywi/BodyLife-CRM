# ADR-004: Module boundaries and business rule ownership

## Статус

Accepted - 2026-07-07.

## Контекст

BodyLife CRM має кілька близьких бізнес-зон: clients, memberships, visits, payments, freezes, non-working days, reports, audit і users/roles. Найбільший ризик - різні екрани або reports почнуть рахувати активність абонемента, залишок занять, мінуси і продовження по-різному.

## Варіанти

- Modules by business capability.
- Layers by technical concern.
- Hybrid: business modules with internal layers.
- CRUD resource modules without explicit domain ownership.

## Рішення

Top-level структура v1 організована навколо бізнес-модулів:

- Clients/Search;
- MembershipTypes;
- Memberships;
- Visits;
- Payments;
- Freezes;
- NonWorkingDays;
- Reports;
- Audit;
- Users/Roles.

Memberships володіє всіма формулами абонемента:

- active status;
- remaining visits;
- negative balance;
- first negative visit date;
- freeze/non-working overlap;
- effective end date;
- membership warnings.

Reports, UI, Visits, Payments, Freezes і NonWorkingDays не рахують ці значення самостійно. Вони використовують public commands/queries Memberships.

Freezes і NonWorkingDays володіють source records для причин продовження, але Memberships обчислює effective membership state.

Окремий Extensions module у v1 не вводиться.

## Наслідки

- Канонічна доменна логіка має одну точку ownership.
- Reports можуть бути узгодженими з client profile і history.
- Audit стає частиною workflow, а не технічним додатком.
- Модулі потребують dependency rules і public interfaces.

## Що це означає для реалізації

- Заборонити direct cross-module writes поза owned workflows.
- Shared concepts обмежити IDs/value objects: `ClientId`, `MembershipId`, `Money`, `DateRange`, `ActorId`.
- Reports реалізувати як query/report layer над canonical records або read models.
- Business formulas не писати в templates, controllers або frontend state.
- Перед додаванням нового top-level модуля перевіряти, чи це справді окрема бізнес-відповідальність.
