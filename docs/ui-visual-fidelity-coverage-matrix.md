# Матриця охоплення візуальної відповідності UI

Дата: 2026-07-22
Оновлено: 2026-08-12 — current-Home is the authorized production migration
target; revised Wave 1 is approved, the fragmented Wave 2 render is rejected,
and the unified Search/Clients-canvas replacement is the reviewed candidate.
Статус: **Waves 0–1 approved; Wave 2 candidate awaiting explicit product-owner
approval**. Waves 3–6 are blocked by their preceding anchor approvals. Це
acceptance ledger для плану
`ui-visual-fidelity-migration-plan.md`; `approved` означає explicit side-by-side
product-owner approval, не лише green automated tests.

## Editable-lab migration inventory (non-acceptance)

All 16 static HTML pages are in the current-Home system migration: Home;
Clients/Profile; Daily, Ending soon, Low remaining, Negative clients and
Inactive clients reports; Audit timeline; Client history; Membership types,
Non-working days and Staff accounts Owner pages; Login, Access denied, Logged
out and Error public states.
`clients.html?state=create-client#client-create` is the direct CreateClient
anchor (header and no-result reachable);
`clients.html?state=cancel-visit#cancel-visit` is the CancelVisit anchor from an
eligible history row, with
`clients.html?state=cancel-visit-stale#cancel-visit-stale` preserving prior
canonical context. This is template-lab
coverage only. Production migration is now authorized, but each remaining
unapproved real Razor row requires the evidence below.

Production acceptance compares each real authenticated Razor route/state with
the evolved current-Home composition and its shared token contract. Locked
PNGs remain immutable historical context and hash provenance; they are not an
exact layout or pixel target for the editable lab or a substitute for explicit
product-owner review.

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

| Anchor | Historical reference context | Required canonical state | Status | Required approval |
| --- | --- | --- | --- | --- |
| Reception home desktop | `branded-light-1-reception-home.png` 1104x789 | Activity query fulfilled; Today metrics via Kyiv `GenerateDailyReport`; Attention counts via `GetReceptionAttentionSummary`; unavailable is not zero; account/session/device visible | approved 2026-08-12 | `/tmp/bodylife-production-wave1-refined-v2/wave1-home-desktop-1440x900-uk.png` and `wave1-home-tablet-1024x768-uk.png`; explicit product-owner approval recorded |
| Client search desktop | `branded-light-2-client-search.png` 1104x773 | `SearchClients`: exact, multiple, no-result, query error; direct Create Client | replacement candidate — approval pending | `/tmp/bodylife-wave2-unified-final-root/desktop-search-idle.png`, `desktop-multiple-results.png`, `desktop-no-results-create.png` and `desktop-direct-create.png`; one header Search + one Clients canvas; zero-P0–P3 closure review |
| Client profile desktop | `branded-light-3-client-profile.png` 1104x1134 | `GetClientProfile`, warnings, allowed actions, active/zero/negative/expired/ending/low/inactive; exact ordinary sale, oldest-first negative coverage and issued-sale correction remain visible | not started | Current-Home composition + real canonical route/state gallery |
| Create Client desktop | `branded-light-4-create-client.png` 1104x1011 | Create validation, duplicate acknowledgement, permission, success canonical reread | replacement candidate — approval pending | `/tmp/bodylife-wave2-unified-final-root/desktop-direct-create.png` plus `/tmp/bodylife-production-wave2-unified-v5/tablet-create-client-duplicate-review.png`; real command validation/busy/success/canonical-reread coverage |
| Cancel Visit desktop | `branded-light-5-cancel-visit.png` 1104x861 | reason/confirmation, permission, stale/concurrency, canceled and backfill/fallback labels | not started | Dedicated current-Home correction anchor + real command states |
| Reception home phone | `branded-mobile-home.png` 480x2450 | single operational column; preserved activity warning/provenance/action order plus global Search/direct Create and Today | approved 2026-08-12 | `/tmp/bodylife-production-wave1-refined-v2/wave1-home-phone-390x844-uk.png`; explicit product-owner approval recorded |
| Client profile phone | `branded-mobile-profile.png` 480x2381 | warnings/actions/context order and wrapping, including negative coverage and sale correction with no hidden consequences | not started | Current-Home single-column composition + real canonical states |
| Cancel Visit phone | `branded-mobile-cancel.png` 480x1818 | expanded danger/correction card and keyboard/focus order | not started | Current-Home single-column correction anchor + real command states |

## Visual route ledger — 15 pages / 16 route entries

All rows start `not started`; record `in progress` only while its one assigned
writer holds the wave. Anchor rows need explicit side-by-side approval before
the next wave. Non-anchor rows become `approved` only after automated evidence,
independent review and final product-owner gallery sign-off. Put links or
artifact paths in the Evidence column; `—` never means approved.

| Route | Wave | Actors | Mandatory fixtures/states | Status | Evidence / approval |
| --- | ---: | --- | --- | --- | --- |
| `/` (separate Reception Home) | 1 | Owner, named Admin, shared Admin | dashboard default/empty/loading/success; Activity and Attention unavailable; exact Home active state | approved 2026-08-12 | PostgreSQL-backed populated/empty/Activity-Attention-Today unavailable matrix, target captures, zero-P0–P3 closure review and explicit product-owner approval |
| `/Reception/Index` | 1–3 | Owner, named Admin, shared Admin | Clients active state; search/profile/direct-create anchors; exact/multiple/no-result/error/stale; exact sale Payment, oldest-first negative coverage, sale replace/cancel and their validation/permission/blocker states | Wave 1 approved; Wave 2 unified replacement candidate; Wave 3 not started | Exactly one header-owned Search and one mutually exclusive-state Clients canvas at 1440/1024/390, full 144/144 UI regression and zero-P0–P3 review; Profile/action visual migration remains pending |
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
| Reception `_ReceptionWorkspace.cshtml` | 2–3 | Clients workspace empty/loading/exact/multiple/no-result/error; stable targets; Home is now the separate root page | Wave 2 unified canvas candidate; Wave 3 profile/actions pending | Real idle/exact/multiple/no-result/failure states replace inside one canvas at desktop/tablet/phone; stable ids/hx/fallback, sticky-header scroll recovery and no-JS checks pass |
| Reception `_ClientProfile.cshtml` | 3 | unavailable/active/zero/negative/expired/ending/low/inactive; actions/history/context; ADR-018 coverage and issued-sale correction reachability | not started | — |
| Reception `_CreateClientForm.cshtml` | 2 | direct open; validation; duplicate review/ack; permission; busy; success collapse | replacement candidate — approval pending | Direct fragment landing inside the unified canvas, validation, duplicate acknowledgement/reason, busy/idempotency and canonical success reread pass; final desktop/tablet/phone captures |
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
| `_Layout.cshtml` | 1, 5 | authenticated/public shells; skip link; main landmarks; no overflow | Wave 1 authenticated approved; public final pending Wave 5 | Authenticated tablet/phone shell plus Login/AccessDenied/Error no-regression matrix passed |
| `_AppNavigation.cshtml` | 1, 4–5 | Home/Clients/Report/History mapping; exact/location state; Owner tools; logout | Wave 1 approved | Rail/drawer exact/location states, Owner denial, inert/overlay/Escape/forward-reverse focus loop passed |
| `_CurrentSession.cshtml` | 1 | Owner/named/shared labels; fixed/masked long session/device ids; phone order | approved 2026-08-12 | Owner, named Admin and shared Reception/Admin account-menu identity/device/session evidence passed |
| `_LanguageSelector.cshtml` | 1, 5 | uk-UA/en-US; long labels; POST success/failure; keyboard/focus | Wave 1 authenticated approved; public final pending Wave 5 | Authenticated account-menu placement and full localization regression passed |
| `_Icon.cshtml` | 1–5 | local sprite, accessible labels where needed, semantic color not sole signal | Wave 1 Home/navigation slice approved | Local sprite, visible semantic labels and contrast checks passed; later waves extend route coverage |

## Wave 1 revised and approved evidence — 2026-08-11/12

- Real Razor/PostgreSQL captures under
  `/tmp/bodylife-production-wave1-refined-v2/`: desktop `1440x900`, tablet
  `1024x768` and phone `390x844`, plus full-page variants.
- `ReceptionHomeSmokeTests` covers the populated canonical Home, exact
  provenance, workflow reachability, rendered timestamp contrast and the full
  actor/culture/viewport default matrix.
- `ReceptionHomeStateSmokeTests` covers all three actor kinds, both cultures
  and both target viewports, then independently forces Activity, Attention and
  Today fail-closed states through an isolated real PostgreSQL fixture.
- `UiStyleCoverageSmokeTests` covers drawer/account keyboard behavior, role
  navigation, Search contracts, public Login/AccessDenied/Error regression,
  target sizes, semantic contrast and overflow.
- The complete authenticated Playwright regression suite passes 141/141; its
  pre-existing route tests now navigate through the responsive drawer and
  scope Reception Search/account-menu assertions to their canonical islands.
- Independent closure review found zero P0–P3 issues after correcting the
  rejected render's font hierarchy, icon rail, tablet header gap, skip link,
  account labels and tertiary contrast. The product owner explicitly approved
  the revised Wave 1 composition on 2026-08-12.

## Wave 2 unified Search/Create replacement evidence — 2026-08-12

- The product owner rejected the first Wave 2 candidate because its Search was
  capped in the header and duplicated by a page-local focal card, while
  Results, Create and an empty Profile column were fragmented into peer
  surfaces. The old `candidate-v3` gallery is superseded.
- Final Razor/PostgreSQL viewport captures under
  `/tmp/bodylife-wave2-unified-final-root/` cover Search idle, exact, multiple,
  no-result and direct Create at `1440x900`, `1024x768` and `390x844`.
  Typed failure and command-state evidence remains under
  `/tmp/bodylife-production-wave2-unified-v5/` from the same unified layout.
- Clients now renders exactly one route-local header Search beside Create and
  one full-width canvas whose Results, Create and Profile states replace one
  another. No duplicate Search, peer raised state cards or empty Profile
  column remains.
- Stable ids, fallback links, htmx targets/sync/indicators, input names,
  antiforgery, duplicate acknowledgement, busy/idempotency and canonical
  reread contracts remain covered. Search and Profile swaps recover below the
  sticky header after deep scrolling.
- Release build passes with zero warnings/errors; focused responsive
  Search/Profile passes 3/3, Home compatibility passes 4/4 and the complete
  authenticated Playwright regression passes 144/144.
- Independent correctness/interface review reports zero remaining P0–P3
  findings. This replacement is a candidate, not an approval; Wave 3 remains
  blocked.

## Superseded production candidate evidence

- All 12 locked Phase 0 artifacts match their recorded SHA-256 values.
- `ReceptionHomeSmokeTests` renders canonical seeded Activity, Today and
  Attention data at both target and delivered native CSS widths. It asserts
  exact active navigation, one main landmark, truthful session context,
  direct-create reachability, distinct occurred/recorded provenance, 44px
  actions, visible focus, responsive order and no horizontal overflow.
- Shared-shell and localization regression checks cover Owner tablet, Admin
  phone and both `uk-UA`/`en-US`; read-contract and PostgreSQL query tests cover
  typed errors, cursor integrity and canonical source behavior.
- Independent review found no behavioral P0, and the distinct occurred time for
  backfill/fallback remains a required invariant. This evidence proves the
  functional baseline only. Its sidebar/top-context/Quick Search assertions
  must be rewritten in Wave 1 and cannot approve the new current-Home candidate.

## Per-row execution checklist

- [ ] Render deterministic seed with required culture, role, Kyiv time and viewport.
- [ ] Assert DOM structure/computed styles plus semantic reading order.
- [ ] Assert all interactive targets are at least 44x44, focus is visible and keyboard path works.
- [ ] Assert contrast and no horizontal/page overflow for long labels and IDs.
- [ ] Assert stable IDs, routes, input names, antiforgery, `hx-*` and `data-busy-*` contracts remain unchanged.
- [ ] Mask volatile values; capture named artifact; use stable-region diff only as diagnostic.
- [ ] Run behavioral checks; compare against the evolved current-Home
  composition at target viewports and use locked references only as historical
  decision context.
- [ ] Record evidence paths, review finding and P0/P1 result; P0/P1 must be zero.
- [ ] Record anchor approval or final gallery product-owner approval before marking `approved`.
