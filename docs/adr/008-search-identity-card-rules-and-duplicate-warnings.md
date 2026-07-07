# ADR-008: Search identity, card rules and duplicate warnings

## Статус

Accepted - 2026-07-07.

## Контекст

Пошук на рецепції є одним з головних драйверів v1. Потрібні card number, ПІБ, phone, last 4 digits і duplicate warnings. Barcode/QR/NFC/turnstile не входять у v1, але card number лишається основним ідентифікатором.

## Варіанти

- Exact card-number first search.
- Full-text-like name/phone search.
- Structured search by fields.
- Fuzzy matching for duplicates.
- Unique active card number constraint.
- Multiple historical card assignments.

## Рішення

Clients/Search володіє:

- пошуком клієнтів;
- current card number;
- phone normalization;
- last 4 phone digits;
- duplicate warnings.

Searchable identifiers:

- current card number;
- surname/name/patronymic/full name;
- normalized phone;
- last 4 digits of phone.

Card rules:

- клієнт може існувати без номера картки;
- current card number, якщо заповнений, унікальний серед current assignments;
- одна картка не може бути прив'язана до двох клієнтів одночасно;
- card number можна змінити або перевидати тільки явною дією з audit;
- historical card assignments дозволені, але тільки одна current assignment активна для номера.

Search behavior:

- exact card-number match має пріоритет;
- якщо exact match єдиний, можна швидко відкрити compact client/profile після submit/scan;
- якщо збіг неунікальний або запит неповний, показувати список результатів;
- пошук без картки працює по ПІБ, телефону і last 4 phone digits.

Duplicate warnings:

- existing current card number блокує створення/зміну;
- duplicate phone або схожий ПІБ попереджають, але не блокують після явного підтвердження;
- merge clients не входить у v1.

Scanner boundary:

- майбутній barcode scanner є лише способом введення того самого card number;
- scanner-specific identity, QR/NFC/turnstile не входять у v1.

## Наслідки

- Reception flow швидко знаходить клієнта за карткою.
- Клієнти без картки не блокуються.
- Duplicate prevention є достатнім для v1 без складного merge workflow.
- Майбутній scanner не змінює доменну модель identity.

## Що це означає для реалізації

- Додати normalized storage/indexes для card number і phone.
- Забезпечити unique constraint для current card assignments.
- Реалізувати duplicate-warning service для create/edit client.
- Додати explicit audit для card reassignment/change.
- Не будувати merge clients, QR/NFC або turnstile integration у v1.
