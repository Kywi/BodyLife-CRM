# ADR-009: Backup, restore and operational recovery

## Статус

Accepted - 2026-07-07.

## Контекст

Система буде на хостингу. Backup робить hosting/provider, restore практично перевіряє власник. Export даних з інтерфейсу у v1 не потрібен. Водночас BodyLife CRM замінює папір, тому втрата даних є операційним ризиком.

## Варіанти

- Hosting-provider managed backups only.
- Managed backups plus owner-visible restore-check procedure.
- App-level export for owner.
- Manual database dumps managed by developer/operator.
- Periodic restore rehearsal in staging/test environment.

## Рішення

BodyLife CRM v1 використовує hosting/provider-managed automated backups як основний backup mechanism, але production-ready вважається тільки після перевіреного restore-check.

Приймаємо:

- managed automated backups;
- documented restore runbook;
- periodic restore rehearsal у staging/test environment;
- owner-visible restore-check checklist;
- paper fallback reconciliation через backdated entries з audit;
- technical logs/status для backup/restore jobs.

Не включаємо у v1:

- app-level export UI для власника;
- backup/restore панель в admin UI;
- developer-only manual dumps як основний backup mechanism.

Очікування v1:

- backup owner: hosting/provider + technical operator/developer;
- restore owner: technical operator/developer;
- restore acceptance: owner проходить checklist;
- retention: мінімум 30 днів automated backups;
- RPO: прагнути до кількох годин/PITR, але не гірше 24 годин;
- RTO: same-business-day restore для production incident;
- перед production use виконати хоча б один restore rehearsal.

## Наслідки

- Backup не є припущенням, доки restore не перевірений.
- Scope v1 не роздувається export UI.
- Business continuity залежить від runbook і дисципліни restore rehearsals.
- Paper fallback після outage має бути узгоджений з ADR-010.

## Що це означає для реалізації

- Підготувати restore runbook до production use.
- Додати technical logging/status для backup/restore operations, якщо це доступно у hosting stack.
- Не додавати export UI в product scope v1.
- Переконатися, що backdated entries підтримують reconciliation після outage.
- Зафіксувати дату і результат першого restore rehearsal.
