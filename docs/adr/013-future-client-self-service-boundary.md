# ADR-013: Future client self-service boundary

## Статус

Accepted - 2026-07-07.

## Контекст

Client self-service не входить у v1 і може взагалі не знадобитися. Водночас архітектура не має зашити membership logic тільки в admin templates/controllers, бо майбутній read-only client view може стати корисним.

## Варіанти

- Ignore client self-service entirely in v1.
- Keep domain/application logic independent from admin UI.
- API-first architecture from day one.
- Later read-only client portal as separate interface.
- Public client account model in v1.

## Рішення

BodyLife CRM v1 залишається internal Owner/Admin web app.

Не входить у v1:

- client self-service;
- client accounts;
- public portal;
- mobile/client-facing API;
- self check-in;
- online payments;
- API-first platform заради гіпотетичного майбутнього portal.

Приймаємо guardrail:

- membership calculations, remaining visits, effective end date, negative balance і warnings мають жити в domain/application layer;
- admin UI читає membership state через application queries/public read methods;
- admin permissions не змішуються з майбутніми client permissions;
- якщо client self-service з'явиться, це буде окремий read-only interface поверх client-safe queries/DTO.

Potential client-safe data для майбутнього ADR:

- membership status;
- remaining visits;
- effective end date;
- negative balance;
- базова історія відвідувань/оплат тільки після privacy decision.

Не виносити назовні без нового ADR:

- audit log;
- admin comments/notes;
- cash reports;
- correction history;
- internal warnings;
- чужі дані або пошук клієнтів.

## Наслідки

- V1 не ускладнюється через непідтверджений future portal.
- Domain/application boundaries лишають можливість майбутнього read-only view.
- Немає зайвого public auth/privacy surface у v1.
- Будь-який реальний client self-service потребуватиме нового ADR.

## Що це означає для реалізації

- Не створювати client auth/account model у v1.
- Не робити API-first/SPA-first вибір лише через можливий portal.
- Не розміщувати membership calculations у templates/controllers.
- Для application queries відділяти domain data від admin-only presentation.
- Якщо з'явиться client view, проєктувати окремі client-safe DTO і privacy rules.
