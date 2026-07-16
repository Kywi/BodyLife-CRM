# BodyLife CRM v1 architecture baseline

Джерело: accepted ADR package у `docs/adr/`, ADR-001..ADR-015, initial package accepted 2026-07-07, ADR-014 accepted 2026-07-14 and ADR-015 accepted 2026-07-16.

Це короткий implementation contract для розробки BodyLife CRM v1. Він не замінює ADR і не вибирає technology stack: мова, framework, database, hosting provider, ORM, queue, UI library та observability vendor мають обиратися окремо. Якщо цей документ конфліктує з ADR, перемагає ADR.

## 1. Короткий висновок

BodyLife CRM v1 - internal hosted web app для одного залу і owner/admin workflows. Головний продуктний сценарій - reception dashboard: швидко знайти клієнта, побачити стан абонемента, відмітити візит, прийняти готівкову оплату, побачити історію і daily report. Основні пристрої - планшет або телефон на рецепції, телефон власника і браузер/desktop за потреби. Інтернет-залежність прийнята явно; при outage бізнес тимчасово повертається до паперу, а потім вносить записи через audited backdated commands. (ADR-001, ADR-003, ADR-010)

Архітектура v1 - modular monolith: один застосунок, один deploy, одна основна transactional system, top-level modules навколо бізнес-відповідальностей. Core workflow має лишатися транзакційно цілісним: visits, payments, membership recalculation, audit і reports не можуть роз'їжджатися. (ADR-002, ADR-004, ADR-005, ADR-007)

UI model - hybrid server-rendered UI: сервер рендерить сторінки, форми, client profile, reports і settings/admin screens; інтерактивність додається тільки там, де вона прискорює рецепцію. Frontend state не є джерелом бізнес-правил. Усі state-changing дії йдуть через server-side commands/actions і після цього UI перечитує canonical state із сервера. (ADR-003, ADR-013)

## 2. Non-negotiable architecture rules

- Можна: будувати один hosted internal web app для одного залу. Не можна: додавати `tenant_id`, SaaS/multi-tenant model, desktop/LAN-first deployment, native mobile app, public client portal або offline-first sync у v1. (ADR-001)
- Можна: modular monolith з бізнес-модулями і внутрішніми layers у межах модуля. Не можна: microservices, distributed workflows, broker-based event infrastructure, full event sourcing або API-first SPA тільки заради гіпотетичного portal. (ADR-002, ADR-013)
- Можна: local in-process events/hooks після успішних commands для audit, recalculation або lightweight read models. Не можна: робити event broker або event sourcing основною архітектурою v1. (ADR-002, ADR-006, ADR-007)
- Можна: виконувати state changes тільки через server-side commands/actions у transaction boundary. Не можна: міняти бізнес-стан прямими database edits, frontend-only state, templates, controllers або ad hoc scripts. (ADR-002, ADR-003, ADR-004, ADR-010)
- Можна: давати модулям public commands/queries. Не можна: робити direct cross-module writes поза owned workflows або копіювати формули між модулями. (ADR-002, ADR-004)
- Можна: мати shared IDs/value objects: `ClientId`, `MembershipId`, `Money`, `DateRange`, `ActorId`. Не можна: створювати shared "god service" для бізнес-правил абонементів. (ADR-004)
- Можна: тримати source facts і централізований derived state для абонементів. Не можна: редагувати `effective_end_date` напряму або рахувати active status, remaining visits, negative balance, first negative visit date, extension days чи warnings поза Memberships. (ADR-004, ADR-005)
- Можна: підтримати negative visits як core membership workflow. Не можна: вводити окремий debt ledger у v1, якщо membership state + explicit closure workflow достатні. (ADR-005)
- Можна: кілька lifecycle-active Memberships із explicit `membership_id` або one-off/trial context у `MarkVisit`. Не можна: автоматично обирати newest/first Membership, створювати мінус без Membership або споживати frozen Membership. (ADR-014)
- Можна: додати Freeze до lifecycle-active Membership, якщо range починається в `membership.start_date..pre-command effective_end_date`; end може вийти за попередню effective end. Не можна: post-expiry/before-start Freeze, silent clipping або overlap з active counted Membership Visit. (ADR-015)
- Можна: Reports як query/report layer поверх canonical records і Memberships queries. Не можна: робити reports окремою доменною правдою, exported snapshots source of truth або дублювати membership formulas у reports. (ADR-007)
- Можна: append-only business audit окремо від technical logs. Не можна: використовувати technical logs як єдине джерело бізнес-історії або UPDATE/DELETE audit entries через application workflows. (ADR-006)
- Можна: backdated entries з окремими `occurred_at` і `recorded_at`, marker `manual_backfill` або `paper_fallback`, actor/account і reason/comment. Не можна: unmarked backdated entries, synthetic fake history, full Excel/paper import або direct DB patch для migration/fallback. (ADR-010)
- Можна: hosting/provider-managed automated backups плюс documented restore runbook і restore rehearsal перед production use. Не можна: вважати backup готовим без перевіреного restore-check або додавати app-level export/backup panel у v1. (ADR-009)
- Можна: Owner, named Admin і shared Reception/Admin account. Не можна: приписувати shared-account action конкретній фізичній людині, якщо система цього не знає. (ADR-012)
- Можна: editable MembershipType catalog плюс immutable snapshot у виданому абонементі. Не можна: hard delete MembershipType або дозволити зміні catalog silently змінювати вже видані абонементи. (ADR-011)

## 3. Module map

- `Clients/Search`: owns client identity, current card number, phone normalization, last 4 phone digits, duplicate warnings and search behavior. Other modules reference clients by ID and do not redefine card/phone duplicate rules. (ADR-008)
- `MembershipTypes`: owns membership type catalog: create/edit/deactivate, owner-only policy, no hard delete, audit. Issuing a membership copies immutable snapshot fields. (ADR-011, ADR-012)
- `Memberships`: owns issued memberships, opening state, recalculation, active status, remaining visits, negative balance, first negative visit date, effective end date, extension days and warnings. This is the only owner of membership formulas. (ADR-004, ADR-005, ADR-011)
- `Visits`: owns visit source records, explicit membership consumptions, one-off/trial contexts, cancellations and visit commands. It never infers a Membership, and must trigger Memberships recalculation and business audit after successful counted state changes. (ADR-005, ADR-006, ADR-007, ADR-014)
- `Payments`: owns payment source records, cash payments, one-off negative closures and payment corrections/cancellations. It must trigger audit and keep reports consistent through canonical records. (ADR-005, ADR-006, ADR-007, ADR-012)
- `Freezes`: owns freeze source records and cancellation/correction workflow. It validates range intent through the Memberships-owned eligibility boundary and does not compute final extension alone; Memberships computes effective state and overlap rules. (ADR-004, ADR-005, ADR-012, ADR-015)
- `NonWorkingDays`: owns non-working day source records and owner-only add/cancel policy. It does not mutate membership end dates directly; Memberships computes extension union days. (ADR-004, ADR-005, ADR-012)
- `Reports`: owns report queries and drill-down views for daily cash/visits, ending-soon memberships, low remaining visits, negative clients and inactive clients. It does not own business formulas. (ADR-007)
- `Audit`: owns append-only business history for successful workflows, corrections, settings changes, backfill and fallback entries. It is separate from technical logs. (ADR-006, ADR-010, ADR-012)
- `Users/Roles`: owns account types, role/policy checks, session/device metadata and correction boundaries. (ADR-012)

Not top-level v1 modules: `Extensions`, separate debt ledger, full import/migration tool, client portal, public client API, online payments, turnstile/barcode/NFC domain integration, complex accounting/full POS. (ADR-001, ADR-004, ADR-005, ADR-008, ADR-010, ADR-013)

## 4. Allowed dependencies

- UI may call application commands/queries and render server state. UI may add quick/live search, compact results, status panels, warnings, quick actions, loading states and duplicate-submit protection. UI may not own business truth. (ADR-003)
- Application commands may coordinate owned module behavior inside one server-side transaction and may call public commands/queries of other modules where ADR ownership requires it. Direct cross-module table writes are forbidden. (ADR-002, ADR-004)
- Visits, Payments, Freezes, NonWorkingDays and backfill/fallback workflows may create source facts through their own commands and must cause Memberships recalculation through Memberships public interfaces/hooks. (ADR-004, ADR-005, ADR-010)
- Membership Visit must submit explicit `membership_id`; one-off/trial creates no consumption. Memberships owns Visit eligibility/warnings, and active inclusive Freeze blocks membership consumption until correction/cancellation. (ADR-014)
- AddFreeze must lock the selected Membership, validate its start against the pre-command canonical effective period, reject active counted Visit overlap and synchronously rebuild state/explanations before success. (ADR-015)
- Memberships may read required canonical source facts or module-provided query results to compute derived membership state. Other modules must read membership state through Memberships public queries. (ADR-004, ADR-005)
- Reports may read canonical source records from Visits, Payments, Memberships and Audit, and may use maintained read models only as a performance optimization. Reports must keep drill-down to source records. (ADR-007)
- Audit may be produced from successful commands/workflows via in-process hooks/events and domain summaries. Audit must not drive business state changes by itself. (ADR-006)
- Technical logs may record errors, latency, request/correlation IDs, auth failures, jobs and backup/restore status. They may correlate with audit through request/correlation ID, but they are not business audit. (ADR-006, ADR-009)
- Membership issue flow may depend on MembershipTypes to copy snapshot values. Historical reports/history must read issued-membership snapshot for historical price/rules. (ADR-011)
- Operations/recovery workflows may depend on hosting/provider backups and documented restore runbook. Paper fallback reconciliation must enter the system through normal domain commands with audit. (ADR-009, ADR-010)

## 5. Forbidden shortcuts

- No business formulas in templates, controllers, frontend state, report SQL/query snippets or duplicated helper functions outside Memberships. (ADR-004, ADR-005, ADR-007, ADR-013)
- No direct editing of `effective_end_date`; every date change must have a source reason: freeze, non-working day, cancellation/correction or explicit adjustment with audit. (ADR-005)
- No direct database edits for migration, paper fallback, correction or operational repair as a product workflow. Use commands, validation, recalculation and audit. (ADR-010)
- No silent correction: cancellations/corrections add new records or correction entries and require reason/comment where business risk requires it. (ADR-006, ADR-012)
- No UPDATE/DELETE of historical audit entries through application workflows. (ADR-006)
- No technical-log-only business history. Owner-readable business audit is required for important workflows. (ADR-006)
- No report-specific reinterpretation of membership state, cash totals, cancellations or corrections. Reports must stay explainable through source records and audit. (ADR-007)
- No exported snapshots as source of truth. Export/report snapshots, if introduced later, are views unless a future ADR says otherwise. (ADR-007)
- No full Excel/paper import, mandatory migration day, synthetic fake history or unmarked backdated entries in v1. (ADR-010)
- No app-level backup/export UI, admin backup panel or developer-only manual dumps as the main backup mechanism in v1. (ADR-009)
- No client accounts, public portal, self check-in, online payments or client-facing API in v1. (ADR-013)
- No merge clients workflow, QR/NFC/turnstile integration or scanner-specific identity model in v1. A scanner, if later used, only enters the same card number. (ADR-008)
- No hard delete for MembershipType and no mutable-reference-only issued membership rules. (ADR-011)
- No pretending shared Reception/Admin audit identifies a physical person. It identifies the shared session/account. (ADR-012)
- No inferred Visit allocation, implicit expired fallback, future-start consumption, membershipless negative state or Visit override on an active Freeze. (ADR-014)
- No Freeze that starts before Membership start, after the locked pre-command effective end or over an active counted Membership Visit; no silent range clipping. (ADR-015)

## 6. Implementation implications

- Start with a modular monolith skeleton and explicit public commands/queries per module before building broad UI. Keep module names aligned with the Module map. (ADR-002, ADR-004)
- Build the first vertical slice around reception dashboard, not generic CRUD: search, client profile/status, record visit, add payment, issue membership, warnings and daily report visibility. (ADR-003, ADR-008)
- Every state-changing command should define: input validation, authorization policy, actor/account/session metadata, transaction boundary, affected source facts, Memberships recalculation need, audit action, reason/comment requirement, duplicate-submit/idempotency guard and error behavior. (ADR-002, ADR-003, ADR-005, ADR-006, ADR-012)
- Use `occurred_at` for business event time and `recorded_at` for system entry time in backfill/fallback-capable commands. Store marker `manual_backfill` or `paper_fallback` where applicable. (ADR-001, ADR-010)
- Keep Memberships recalculation testable outside UI. Required domain coverage includes inclusive end date, canceled visits, negative visits, first negative date, freeze/non-working overlap as union calendar days, backdated entries and correction-triggered recalculation. (ADR-005)
- Before Visit persistence, lock ADR-014 in pure Memberships tests: explicit ambiguous selection, expired acknowledgement, future-start rejection, one-off/trial no-consumption, deterministic Visit ordering and active-Freeze blocking. (ADR-014)
- Before AddFreeze persistence, lock ADR-015 in pure Memberships tests: lifecycle status, inclusive endpoints, before-start/post-expiry rejection, end-after-effective acceptance and active/canceled Visit overlap. (ADR-015)
- Make reports drill-down-first: every total must explain which source records are counted and how cancellations/corrections changed the result. (ADR-007)
- Implement permissions as policy checks on commands, not as hidden UI-only rules. Owner-only and current-day/day-close correction boundaries must be enforceable server-side. (ADR-012)
- Design audit schema as part of workflow implementation, not as a later logging add-on. Required audit fields include actor/account, role, session/device, action type, entity type/id, related IDs, `occurred_at`, `recorded_at`, before/after or domain summary, reason/comment and request/correlation ID. (ADR-006, ADR-012)
- Treat backup/restore and paper fallback as production readiness work: restore runbook, at least one restore rehearsal before production use, owner-visible checklist, backup/restore technical status where available, and clear fallback reconciliation path. (ADR-009, ADR-010)

## 7. Quality gates before coding

- ADR traceability gate: each module boundary, command rule, report rule, audit rule and out-of-scope rejection must cite ADR-001..ADR-015 or stay out of the baseline.
- Scope gate: v1 contains no offline-first sync, multi-tenant/SaaS scope, native mobile app, public client portal, client accounts, online payments, turnstile/NFC/QR identity, full import, complex accounting/full POS or long-period financial reports. (ADR-001, ADR-008, ADR-010, ADR-012, ADR-013)
- Module gate: every state-changing workflow has an owning module and uses public interfaces for cross-module behavior. No direct cross-module writes are allowed. (ADR-002, ADR-004)
- Membership gate: no UI/report/controller logic may calculate membership state independently. Memberships owns formulas and recalculation tests exist before relying on reports/UI. (ADR-004, ADR-005, ADR-007)
- Visit allocation gate: `MarkVisit` has explicit Membership/context, never auto-selects under ambiguity, creates no consumption for one-off/trial and blocks membership consumption during active Freeze. (ADR-014)
- Freeze eligibility gate: `AddFreeze` uses locked pre-command Membership state, rejects invalid lifecycle/range and active counted Visit overlap, then recalculates through Memberships. (ADR-015)
- Command gate: server-side command/action path includes authorization, transaction boundary, duplicate-submit guard, recalculation decision, audit entry and canonical reread for UI. (ADR-002, ADR-003, ADR-005, ADR-006, ADR-012)
- Audit/logging gate: business audit and technical logs have separate storage/semantics; technical logs cannot satisfy business history requirements. (ADR-006)
- Reporting gate: report totals reconcile with source records and provide drill-down. Daily report handles cancellations/corrections and later corrections remain visible. (ADR-007)
- Backfill/fallback gate: backdated entries support `occurred_at` vs `recorded_at`, marker, actor/account and reason/comment; opening state is a valid source fact, not a database patch. (ADR-010)
- Permissions/accountability gate: Owner, named Admin and shared Reception/Admin behavior is enforced and represented honestly in audit. (ADR-012)
- Operations gate: production use waits for managed backup configuration, documented restore runbook, minimum 30-day backup retention expectation, RPO/RTO expectation review, and at least one restore rehearsal with recorded result. (ADR-009)
