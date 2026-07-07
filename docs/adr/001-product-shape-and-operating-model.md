# ADR-001: Product shape and operating model v1

## Статус

Accepted - 2026-07-07.

## Контекст

BodyLife CRM v1 має замінити паперовий і частково Excel-облік абонементів у одному залі. Головний сценарій - рецепція: знайти клієнта, побачити стан абонемента, відмітити візит, прийняти готівкову оплату, побачити історію і денний звіт.

Очікувані пристрої - планшет або телефон на рецепції, телефон власника і, за потреби, desktop/browser. Інтернет вважається достатньо стабільним. Якщо інтернет зникає, бізнес тимчасово повертається до паперу, а потім вносить записи в систему.

## Варіанти

- Internal hosted web app для owner/admin workflows.
- Local/LAN або desktop-first system.
- PWA/offline-first web app.
- Mobile-first або окремий native app.
- SaaS-like multi-tenant platform.

## Рішення

BodyLife CRM v1 є internal hosted web app для одного залу і owner/admin workflows.

Приймаємо:

- основний surface - web app у браузері;
- primary reception devices - планшет або телефон;
- owner access - з телефону або браузера;
- hosted deployment;
- paper fallback при втраті інтернету;
- backdated entries після fallback з обов'язковим business audit.

Не входить у v1:

- offline-first sync;
- desktop/LAN-first deployment;
- native mobile app;
- public client portal;
- SaaS/multi-tenant model;
- online payments, turnstile, barcode/NFC як архітектурна основа;
- complex accounting або full POS.

## Наслідки

- Операційна модель лишається простою: один hosted app і один production environment.
- Власник має зручний віддалений доступ без VPN/LAN.
- Інтернет-залежність прийнята явно, але компенсується паперовим fallback.
- Немає потреби проектувати tenant isolation, offline conflict resolution або native-device lifecycle у v1.
- Production readiness залежить від backup/restore і зрозумілого fallback reconciliation.

## Що це означає для реалізації

- Не вводити `tenant_id`, multi-tenant billing або tenant-scoped admin model у v1.
- Робити responsive UI для tablet-first reception і phone-friendly owner views.
- Усі state-changing дії виконувати на сервері, без offline queue/sync.
- Підтримати `occurred_at` і `recorded_at` для backdated visits/payments/freezes/memberships.
- Підготувати простий operational runbook: hosted deployment, backup, restore-check, paper fallback, reconciliation.
