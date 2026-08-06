# Матриця охоплення візуальної відповідності UI

Дата: 2026-07-22
Оновлено: 2026-08-06 — додано acceptance coverage для Milestone 10.5 / ADR-018.
Статус: **in progress — Wave 1 candidate awaiting product-owner approval**.
Phase 0 is locked; Waves 2–6 are not started. Це acceptance ledger для плану
`ui-visual-fidelity-migration-plan.md`; `approved` означає explicit side-by-side
product-owner approval, не лише green automated tests.

## Обов'язкові осі для кожного route/partial row

| Axis | Mandatory coverage |
| --- | --- |
| Culture | `uk-UA`, `en-US` |
| Actor | Owner, named Admin, shared Reception/Admin — де застосовно |
| Viewport | tablet `1024x768`, phone `390x844`, plus reference-native dimension for the applicable anchor |
| Query/action state | default, empty, loading, exact, multiple, no-result, query error/unavailable (never fake zero), validation, permission, stale, concurrency, success — де застосовно |
| Membership | active, zero, negative, expired, ending, low, inactive — де застосовно |
| Provenance/risk | correction/cancel, manual backfill, paper fallback batch + numbered sheet + stable row + event type — де застосовно |
| Resilience/a11y | long labels/IDs/wrapping, keyboard/focus, 44x44 target, contrast, semantic order, no page overflow |

## Anchor acceptance ledger

| Anchor | Reference | Required canonical state | Status | Required approval |
| --- | --- | --- | --- | --- |
| Reception home desktop | `branded-light-1-reception-home.png` 1104x789 | Activity query fulfilled; Today metrics via Kyiv `GenerateDailyReport`; Attention counts via `GetReceptionAttentionSummary`; unavailable is not zero; account/session/device visible | candidate — awaiting approval | User/product-owner side-by-side of the locked reference and `/tmp/bodylife-wave1-captures/wave1-home-tablet-1024x768-uk.png`; native-width diagnostic: `wave1-home-reference-desktop-736x526-uk.png` |
| Client search desktop | `branded-light-2-client-search.png` 1104x773 | `SearchClients`: exact, multiple, no-result, query error; direct Create Client | not started | User/product-owner side-by-side |
| Client profile desktop | `branded-light-3-client-profile.png` 1104x1134 | `GetClientProfile`, warnings, allowed actions, active/zero/negative/expired/ending/low/inactive; exact ordinary sale, oldest-first negative coverage and issued-sale correction remain visible | not started | User/product-owner side-by-side |
| Create Client desktop | `branded-light-4-create-client.png` 1104x1011 | Create validation, duplicate acknowledgement, permission, success canonical reread | not started | User/product-owner side-by-side |
| Cancel Visit desktop | `branded-light-5-cancel-visit.png` 1104x861 | reason/confirmation, permission, stale/concurrency, canceled and backfill/fallback labels | not started | User/product-owner side-by-side |
| Reception home phone | `branded-mobile-home.png` 480x2450 | single operational column; preserved activity warning/provenance/action order plus Quick Search and Today | candidate — awaiting approval | User/product-owner side-by-side of the locked reference and `/tmp/bodylife-wave1-captures/wave1-home-phone-390x844-uk.png`; native-width diagnostic: `wave1-home-reference-phone-320x844-uk.png` |
| Client profile phone | `branded-mobile-profile.png` 480x2381 | warnings/actions/context order and wrapping, including negative coverage and sale correction with no hidden consequences | not started | User/product-owner side-by-side |
| Cancel Visit phone | `branded-mobile-cancel.png` 480x1818 | expanded danger/correction card and keyboard/focus order | not started | User/product-owner side-by-side |

## Visual route ledger — 15 pages / 16 route entries

All rows start `not started`; record `in progress` only while its one assigned
writer holds the wave. Anchor rows need explicit side-by-side approval before
the next wave. Non-anchor rows become `approved` only after automated evidence,
independent review and final product-owner gallery sign-off. Put links or
artifact paths in the Evidence column; `—` never means approved.

| Route | Wave | Actors | Mandatory fixtures/states | Status | Evidence / approval |
| --- | ---: | --- | --- | --- | --- |
| `/` (separate Reception Home) | 1 | Owner, named Admin, shared Admin | dashboard default/empty/loading/success; Activity and Attention unavailable; exact Home active state | candidate — awaiting approval | `ReceptionHomeSmokeTests` target/native captures; `UiStyleCoverageSmokeTests`; explicit approval pending |
| `/Reception/Index` | 1–3 | Owner, named Admin, shared Admin | Clients active state; search/profile/direct-create anchors; exact/multiple/no-result/error/stale; exact sale Payment, oldest-first negative coverage, sale replace/cancel and their validation/permission/blocker states | in progress — Wave 1 shell/direct-create entry only | Direct `?create=true` opens the permission-gated create panel without search; search/create composition remains Wave 2 and profile/actions remain Wave 3 |
| `/Owner/MembershipTypes` | 4 | Owner; Admin denial | empty/catalog/create/edit/deactivate/validation/permission; immutable `ordinary`/`one_off` kind, positive price, one-off visit limit 1; long bilingual names | not started | — |
| `/Owner/NonWorkingDays` | 4 | Owner; Admin denial | empty/list/preview/confirmation/affected-scope-changed/expired-token/correct/cancel/success | not started | — |
| `/Owner/StaffAccounts` | 4 | Owner; Admin denial | empty/list/create/activate/deactivate/credentials/validation/permission; named/shared labels | not started | — |
| `/Reports/Daily` | 5 | Owner, named Admin, shared Admin | Kyiv date/default/empty/error; visits/payments/corrections; canceled original and exact replacement Payment remain explainable; negative coverage; paper entry origin plus History/Audit drill-down; totals equal active drill rows | not started | — |
| `/Reports/EndingSoon` | 5 | Owner, named Admin, shared Admin | empty/list/filter/pagination/recalculation failure; ending warning | not started | — |
| `/Reports/LowRemaining` | 5 | Owner, named Admin, shared Admin | empty/list/filter/pagination/recalculation failure; zero/low warnings | not started | — |
| `/Reports/NegativeClients` | 5 | Owner, named Admin, shared Admin | empty/list/filter/pagination/recalculation failure; negative warning | not started | — |
| `/Reports/InactiveClients` | 5 | Owner, named Admin, shared Admin | 14/30/60, no-visits, empty/list/error/pagination; inactive status | not started | — |
| `/Audit/Timeline` | 5 | Owner, named Admin, shared Admin | default/empty/filter/error/pagination; long IDs; all origins/corrections; typed sale replace/cancel and negative-coverage lifecycle explanations; paper sheet/row/event; History page active | not started | — |
| `/Audit/ClientHistory` | 5 | Owner, named Admin, shared Admin | no-client/client/empty/filter/error/pagination; original sale/Payment plus replacement/cancel rows; negative coverage create/replace/cancel; exact paper provenance; History location active | not started | — |
| `/Login` | 5 | Anonymous | default/validation/invalid credentials/locked or disabled as supported; both cultures | not started | — |
| `/Logout` | 5 | Authenticated | POST success and resulting public presentation; keyboard/focus | not started | — |
| `/AccessDenied` | 5 | Authenticated/anonymous as routed | permission explanation, safe navigation, both cultures | not started | — |
| `/Error` | 5 | Anonymous/authenticated | safe generic failure, correlation label masking, retry/navigation | not started | — |
| `/SetLanguage` (non-visual POST transport) | 5 | All applicable | antiforgery, supported cultures, local redirect, no open redirect; visual control covered below | not started | — |

## Workflow partial ledger — 16

| Partial | Wave | Mandatory fixtures/states | Status | Evidence / approval |
| --- | ---: | --- | --- | --- |
| Reception `_ReceptionWorkspace.cshtml` | 2–3 | Clients workspace empty/loading/exact/multiple/no-result/error; stable targets; Home is now the separate root page | not started | — |
| Reception `_ClientProfile.cshtml` | 3 | unavailable/active/zero/negative/expired/ending/low/inactive; actions/history/context; ADR-018 coverage and issued-sale correction reachability | not started | — |
| Reception `_CreateClientForm.cshtml` | 2 | direct open; validation; duplicate review/ack; permission; busy; success collapse | not started | — |
| Reception `_UpdateClientForm.cshtml` | 3 | validation; duplicate review/ack; busy; success; permission | not started | — |
| Reception `_CardAssignmentForm.cshtml` | 3 | assign/change/clear; duplicate block; reason; permission/stale/success | not started | — |
| Reception `_MarkVisitForm.cshtml` | 3 | membership/one-off/trial; zero/negative/expired acknowledgement; freeze block; busy/stale | not started | — |
| Reception `_IssueMembershipForm.cshtml` | 3 | active ordinary type; immutable snapshot; one read-only exact cash Payment with no omit/under/over input; leave-visible/new-Membership negative decision; forced oldest start, remainder/expired consequence; busy/stale/success | not started | — |
| Reception `_NegativeVisitCoveragePanel.cshtml` | 3 | no preselected/recommended method; oldest-first concrete Visits; one-off quantities and exact Payment; leave-visible/new-Membership path; closure cancel/same-method replace, restored/replacement facts, reason/confirmation, no refund/delta, permission/stale/success | not started | — |
| Reception `_IssuedMembershipSaleCorrectionForm.cshtml` | 3 | original snapshot/exact Payment; cancel/replace with no default; replacement ordinary type/start; visits/freezes/non-working/coverage dependencies and blockers; reason/confirmation; no refund/delta; paper provenance; permission/stale/changed-after-close/success | not started | — |
| Reception `_AddPaymentForm.cshtml` | 3 | one-off/trial/other only; ordinary sale and negative closure rejected in favor of their full workflows; normal/backfill/fallback; decimal validation; busy/duplicate/success | not started | — |
| Reception `_CorrectPaymentForm.cshtml` | 3 | standalone accepted contexts only; sale/negative-closure correction redirected to full workflows; replace/cancel; reason/confirmation; permission/stale/changed-after-close/success | not started | — |
| Reception `_AddFreezeForm.cshtml` | 3 | eligible/overlap/visit block; backfill; busy/stale/success | not started | — |
| Reception `_CancelFreezeForm.cshtml` | 3 | reason/confirmation; permission/stale/changed-after-close/success | not started | — |
| Reception `_CancelVisitForm.cshtml` | 3 | expanded reference state; reason/confirmation; permission/stale/concurrency/backfill/fallback/success | not started | — |
| Owner `_NonWorkingDayPreviewWorkspace.cshtml` | 4 | input/preview/impact/confirmation/token expiry/scope change/success | not started | — |
| Owner `_NonWorkingDayCorrectionWorkspace.cshtml` | 4 | replace/cancel preview/confirmation/token expiry/scope change/success | not started | — |

## Shared composition partial ledger — 5

| Partial | Wave | Mandatory fixtures/states | Status | Evidence / approval |
| --- | ---: | --- | --- | --- |
| `_Layout.cshtml` | 1, 5 | authenticated/public shells; skip link; main landmarks; no overflow | Wave 1 candidate — awaiting approval | Home authenticated shell is covered at target/native widths; public-shell final gallery remains Wave 5 |
| `_AppNavigation.cshtml` | 1, 4–5 | Home/Clients/Report/History mapping; exact/location state; Owner tools; logout | Wave 1 candidate — awaiting approval | Exact Home/Clients and section Report/History state, Owner disclosure, logout reachability and 44px targets are covered; later-route gallery remains pending |
| `_CurrentSession.cshtml` | 1 | Owner/named/shared labels; fixed/masked long session/device ids; phone order | Wave 1 candidate — awaiting approval | Truthful account kind, device and session remain visible in Home desktop/phone captures |
| `_LanguageSelector.cshtml` | 1, 5 | uk-UA/en-US; long labels; POST success/failure; keyboard/focus | Wave 1 candidate — awaiting approval | Compact UA/EN control retains full accessible labels; localization regression coverage passes |
| `_Icon.cshtml` | 1–5 | local sprite, accessible labels where needed, semantic color not sole signal | Wave 1 candidate — awaiting approval | Home/navigation icon slice covered; Waves 2–6 still require their own row evidence |

## Wave 1 checkpoint evidence

- All 12 locked Phase 0 artifacts match their recorded SHA-256 values.
- `ReceptionHomeSmokeTests` renders canonical seeded Activity, Today and
  Attention data at both target and delivered native CSS widths. It asserts
  exact active navigation, one main landmark, truthful session context,
  direct-create reachability, distinct occurred/recorded provenance, 44px
  actions, visible focus, responsive order and no horizontal overflow.
- Shared-shell and localization regression checks cover Owner tablet, Admin
  phone and both `uk-UA`/`en-US`; read-contract and PostgreSQL query tests cover
  typed errors, cursor integrity and canonical source behavior.
- Independent review found no P0. Its one P1 (missing distinct occurred time
  for backfill/fallback) was fixed and locked by Playwright assertions. This
  ledger deliberately remains unapproved until the side-by-side user decision.

## Per-row execution checklist

- [ ] Render deterministic seed with required culture, role, Kyiv time and viewport.
- [ ] Assert DOM structure/computed styles plus semantic reading order.
- [ ] Assert all interactive targets are at least 44x44, focus is visible and keyboard path works.
- [ ] Assert contrast and no horizontal/page overflow for long labels and IDs.
- [ ] Assert stable IDs, routes, input names, antiforgery, `hx-*` and `data-busy-*` contracts remain unchanged.
- [ ] Mask volatile values; capture named artifact; use stable-region diff only as diagnostic.
- [ ] Run behavioral checks; inspect side-by-side against locked reference at native and target viewport.
- [ ] Record evidence paths, review finding and P0/P1 result; P0/P1 must be zero.
- [ ] Record anchor approval or final gallery product-owner approval before marking `approved`.
