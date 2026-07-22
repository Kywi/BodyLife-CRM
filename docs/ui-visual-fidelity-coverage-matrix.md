# Матриця охоплення візуальної відповідності UI

Дата: 2026-07-22
Статус: **not started**. Це acceptance ledger для плану
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
| Provenance/risk | correction/cancel, manual backfill, paper fallback — де застосовно |
| Resilience/a11y | long labels/IDs/wrapping, keyboard/focus, 44x44 target, contrast, semantic order, no page overflow |

## Anchor acceptance ledger

| Anchor | Reference | Required canonical state | Status | Required approval |
| --- | --- | --- | --- | --- |
| Reception home desktop | `branded-light-1-reception-home.png` 1104x789 | Activity query fulfilled; Today metrics via Kyiv `GenerateDailyReport`; Attention counts via `GetReceptionAttentionSummary`; unavailable is not zero; account/session/device visible | not started | User/product-owner side-by-side |
| Client search desktop | `branded-light-2-client-search.png` 1104x773 | `SearchClients`: exact, multiple, no-result, query error; direct Create Client | not started | User/product-owner side-by-side |
| Client profile desktop | `branded-light-3-client-profile.png` 1104x1134 | `GetClientProfile`, warnings, allowed actions, active/zero/negative/expired/ending/low/inactive | not started | User/product-owner side-by-side |
| Create Client desktop | `branded-light-4-create-client.png` 1104x1011 | Create validation, duplicate acknowledgement, permission, success canonical reread | not started | User/product-owner side-by-side |
| Cancel Visit desktop | `branded-light-5-cancel-visit.png` 1104x861 | reason/confirmation, permission, stale/concurrency, canceled and backfill/fallback labels | not started | User/product-owner side-by-side |
| Reception home phone | `branded-mobile-home.png` 480x2450 | single operational column; default/empty/loading/search states | not started | User/product-owner side-by-side |
| Client profile phone | `branded-mobile-profile.png` 480x2381 | warnings/actions/context order and wrapping | not started | User/product-owner side-by-side |
| Cancel Visit phone | `branded-mobile-cancel.png` 480x1818 | expanded danger/correction card and keyboard/focus order | not started | User/product-owner side-by-side |

## Visual route ledger — 15 pages / 16 route entries

All rows start `not started`; record `in progress` only while its one assigned
writer holds the wave. Anchor rows need explicit side-by-side approval before
the next wave. Non-anchor rows become `approved` only after automated evidence,
independent review and final product-owner gallery sign-off. Put links or
artifact paths in the Evidence column; `—` never means approved.

| Route | Wave | Actors | Mandatory fixtures/states | Status | Evidence / approval |
| --- | ---: | --- | --- | --- | --- |
| `/` (Reception root alias) | 1 | Owner, named Admin, shared Admin | dashboard default/empty/loading/success; Activity and Attention unavailable; exact Home active state | not started | — |
| `/Reception/Index` | 1–3 | Owner, named Admin, shared Admin | Clients active state; search/profile/direct-create anchors; exact/multiple/no-result/error/stale | not started | — |
| `/Owner/MembershipTypes` | 4 | Owner; Admin denial | empty/catalog/create/edit/deactivate/validation/permission; long bilingual names | not started | — |
| `/Owner/NonWorkingDays` | 4 | Owner; Admin denial | empty/list/preview/confirmation/affected-scope-changed/expired-token/correct/cancel/success | not started | — |
| `/Owner/StaffAccounts` | 4 | Owner; Admin denial | empty/list/create/activate/deactivate/credentials/validation/permission; named/shared labels | not started | — |
| `/Reports/Daily` | 5 | Owner, named Admin, shared Admin | Kyiv date/default/empty/error; visits/payments/corrections; totals equal drill rows | not started | — |
| `/Reports/EndingSoon` | 5 | Owner, named Admin, shared Admin | empty/list/filter/pagination/recalculation failure; ending warning | not started | — |
| `/Reports/LowRemaining` | 5 | Owner, named Admin, shared Admin | empty/list/filter/pagination/recalculation failure; zero/low warnings | not started | — |
| `/Reports/NegativeClients` | 5 | Owner, named Admin, shared Admin | empty/list/filter/pagination/recalculation failure; negative warning | not started | — |
| `/Reports/InactiveClients` | 5 | Owner, named Admin, shared Admin | 14/30/60, no-visits, empty/list/error/pagination; inactive status | not started | — |
| `/Audit/Timeline` | 5 | Owner, named Admin, shared Admin | default/empty/filter/error/pagination; long IDs; all origins/corrections; History page active | not started | — |
| `/Audit/ClientHistory` | 5 | Owner, named Admin, shared Admin | no-client/client/empty/filter/error/pagination; correction/cancel/backfill/fallback; History location active | not started | — |
| `/Login` | 5 | Anonymous | default/validation/invalid credentials/locked or disabled as supported; both cultures | not started | — |
| `/Logout` | 5 | Authenticated | POST success and resulting public presentation; keyboard/focus | not started | — |
| `/AccessDenied` | 5 | Authenticated/anonymous as routed | permission explanation, safe navigation, both cultures | not started | — |
| `/Error` | 5 | Anonymous/authenticated | safe generic failure, correlation label masking, retry/navigation | not started | — |
| `/SetLanguage` (non-visual POST transport) | 5 | All applicable | antiforgery, supported cultures, local redirect, no open redirect; visual control covered below | not started | — |

## Workflow partial ledger — 14

| Partial | Wave | Mandatory fixtures/states | Status | Evidence / approval |
| --- | ---: | --- | --- | --- |
| Reception `_ReceptionWorkspace.cshtml` | 1–3 | dashboard/client modes; empty/loading/exact/multiple/no-result/error; stable targets | not started | — |
| Reception `_ClientProfile.cshtml` | 3 | unavailable/active/zero/negative/expired/ending/low/inactive; actions/history/context | not started | — |
| Reception `_CreateClientForm.cshtml` | 2 | direct open; validation; duplicate review/ack; permission; busy; success collapse | not started | — |
| Reception `_UpdateClientForm.cshtml` | 3 | validation; duplicate review/ack; busy; success; permission | not started | — |
| Reception `_CardAssignmentForm.cshtml` | 3 | assign/change/clear; duplicate block; reason; permission/stale/success | not started | — |
| Reception `_MarkVisitForm.cshtml` | 3 | membership/one-off/trial; zero/negative/expired acknowledgement; freeze block; busy/stale | not started | — |
| Reception `_IssueMembershipForm.cshtml` | 3 | preview; inactive type; negative decision; payment; busy/stale/success | not started | — |
| Reception `_AddPaymentForm.cshtml` | 3 | normal/backfill/fallback; decimal validation; busy/duplicate/success | not started | — |
| Reception `_CorrectPaymentForm.cshtml` | 3 | replace/cancel; reason/confirmation; permission/stale/changed-after-close/success | not started | — |
| Reception `_AddFreezeForm.cshtml` | 3 | eligible/overlap/visit block; backfill; busy/stale/success | not started | — |
| Reception `_CancelFreezeForm.cshtml` | 3 | reason/confirmation; permission/stale/changed-after-close/success | not started | — |
| Reception `_CancelVisitForm.cshtml` | 3 | expanded reference state; reason/confirmation; permission/stale/concurrency/backfill/fallback/success | not started | — |
| Owner `_NonWorkingDayPreviewWorkspace.cshtml` | 4 | input/preview/impact/confirmation/token expiry/scope change/success | not started | — |
| Owner `_NonWorkingDayCorrectionWorkspace.cshtml` | 4 | replace/cancel preview/confirmation/token expiry/scope change/success | not started | — |

## Shared composition partial ledger — 5

| Partial | Wave | Mandatory fixtures/states | Status | Evidence / approval |
| --- | ---: | --- | --- | --- |
| `_Layout.cshtml` | 1, 5 | authenticated/public shells; skip link; main landmarks; no overflow | not started | — |
| `_AppNavigation.cshtml` | 1, 4–5 | Home/Clients/Report/History mapping; exact/location state; Owner tools; logout | not started | — |
| `_CurrentSession.cshtml` | 1 | Owner/named/shared labels; fixed/masked long session/device ids; phone order | not started | — |
| `_LanguageSelector.cshtml` | 1, 5 | uk-UA/en-US; long labels; POST success/failure; keyboard/focus | not started | — |
| `_Icon.cshtml` | 1–5 | local sprite, accessible labels where needed, semantic color not sole signal | not started | — |

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
