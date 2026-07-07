# ADR-012: Permissions matrix, session accountability and correction boundaries

## Статус

Accepted - 2026-07-07.

## Контекст

V1 має Owner і Admin. Окремого Trainer role немає. Довірений тренер або заміна адміністратора може працювати з рецепції, але система не повинна вдавати, що знає фізичну людину, якщо використовується shared account.

Owner-only actions: типи абонементів, неробочі дні, небезпечні масові або службові дії. Admin має виконувати щоденні операції без очікування власника.

## Варіанти

- Two roles: Owner/Admin.
- Owner/Admin plus simplified trusted-staff role.
- Shared reception account with named admin accounts.
- Shared login without strong actor clarity.
- Current-day admin corrections plus owner-only after day close.
- No day close, all corrections by role.

## Рішення

BodyLife CRM v1 використовує:

- Owner account;
- named Admin account;
- shared Reception/Admin account.

Accounts:

- Owner - персональний акаунт власника.
- Named Admin - персональний акаунт основного адміністратора.
- Shared Reception/Admin - спільний акаунт для робочого девайса, тренера або заміни адміністратора.

Правила роботи:

- основний адмін працює під named Admin account;
- якщо адмін іде зі зміни або залишає пристрій іншій людині, він виходить зі свого акаунта;
- тренер/заміна/людина на рецепції входить у shared Reception/Admin account;
- не дозволяється працювати під персональним акаунтом іншої людини.

Permissions v1:

- Owner-only: create/edit/deactivate MembershipType, add/cancel NonWorkingDays, dangerous mass actions, службові налаштування.
- Admin + Owner: create/edit client, issue membership, take payment, record visit including negative, cancel mistaken visit, add/cancel freeze, view daily cash report.
- Admin + Owner current-day: correct/cancel payment or freeze with audit reason.
- After day close/reconciliation: corrections мають бути owner-only або explicit owner-approved policy.
- Long-period financial reports не входять у v1.

Audit:

- named Admin audit означає відповідальність конкретного admin account;
- shared Reception/Admin audit означає дію зі shared reception session;
- audit фіксує account_id, account_type, role, device/session, action, occurred_at, recorded_at, before/after або summary, reason/comment;
- система не приписує shared-account action конкретній фізичній людині.

## Наслідки

- Щоденний workflow не блокується owner-only політиками.
- Dangerous actions лишаються під контролем власника.
- Audit чесно відображає рівень identity, який система реально знає.
- Shared account знижує точність accountability, але робить заміну адміністратора реалістичною для v1.

## Що це означає для реалізації

- Ввести role/policy checks для кожної state-changing action.
- Account model має відрізняти `owner`, `named_admin`, `shared_reception_admin`.
- У session metadata зберігати device/session id для audit.
- Для corrections додати reason/comment і current-day/day-close policy.
- UI має явно показувати, під яким account працює рецепція.
