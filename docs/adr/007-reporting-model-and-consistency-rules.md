# ADR-007: Reporting model and consistency rules

## Статус

Accepted - 2026-07-07.

## Контекст

V1 reports мають покривати денну касу/візити, абонементи, що скоро закінчуються, низький залишок занять, мінусових клієнтів і неактивних клієнтів. Daily report має узгоджуватися з visits/payments і враховувати cancellations/corrections.

## Варіанти

- Reports as direct queries over source records.
- Reports as maintained read models.
- Reports as exported snapshots.
- Daily cash report with open/closed day lifecycle.
- No closed day, always live recalculation.

## Рішення

Reports у v1 є query/report layer поверх canonical source records:

- Visits;
- Payments;
- Memberships;
- Audit.

Reports не мають власних formulas для active status, remaining visits, negative balance або end dates. Вони читають ці значення через Memberships public queries.

Основний підхід v1 - live direct queries over source records. Maintained read models дозволені тільки як оптимізація, якщо звіти стануть повільними. Exported snapshots не є source of truth.

Daily report за вибрану дату:

- visits count = зараховані, не скасовані візити;
- payments count = чинні, не скасовані оплати;
- cash sum = сума чинних готівкових оплат;
- кожен підсумок має drill-down список записів.

Day lifecycle мінімальний:

- поточний день open;
- після звірки/close cash day corrections дозволені тільки за permissions;
- close/reconciliation фіксує reconciliation point, але не заморожує правду;
- later corrections мають бути видимі у report/audit.

V1 reports:

- daily cash/visits;
- memberships ending soon: `days_left <= 7`;
- low remaining visits: `remaining_visits <= 2`;
- negative clients;
- inactive clients with thresholds `14 / 30 / 60 days`.

## Наслідки

- Daily totals можуть змінитися після correction, але зміна буде пояснюваною.
- Reports лишаються узгодженими з client history.
- Heavy read model не додається без performance need.
- Advanced monthly financial reporting лишається поза v1.

## Що це означає для реалізації

- Реалізувати Reports як application/query services, а не як окрему доменну правду.
- Кожен report total має мати drill-down до source records.
- Під час correction/cancellation оновлювати live report results через canonical records.
- Додати consistency tests: daily report vs payment history, daily visits vs visit history, report membership state vs client profile.
- Не робити exported snapshots джерелом істини.
