# План міграції current-Home шаблону в production Razor UI

Дата: 2026-07-22
Оновлено: 2026-08-12
Статус: **production migration authorized; Waves 0–2 approved; Wave 3
Profile/actions candidate completed and awaiting explicit product-owner
approval; Wave 4 blocked**.

## Product decision and reset point

The product owner approved the evolved current-Home template system as the
composition and visual target for production migration. The target is defined
by `.interface-design/system.md` together with the complete review inventory in
`docs/ui-prototype/`. It is a light operational interface with a 240px labeled
desktop rail, tablet drawer, sticky global header, direct Search/Create Client,
honest account menu, Activity-first Home and semantic blue/green/amber/red/
violet guidance.

The previously implemented production Wave 1 candidate used an earlier
sidebar/top-context/Quick Search composition. It remains a valid functional
baseline, but its visual composition is **superseded and not approved**. Its
server-owned `GetReceptionActivity`, `GetReceptionAttentionSummary` and
`GenerateDailyReport` reads stay in place. No old visual assertion, green test
or locked reference image may silently approve that superseded composition.

Approval of the template authorizes this migration. It does not pre-approve a
future Razor rendering: after each anchor wave, the real authenticated
tablet/phone candidate must still be shown for product-owner acceptance before
the next anchor wave starts.

## Source hierarchy

Use the following order when a visual example and application behavior differ:

1. Accepted ADRs, domain rules, interaction contracts and authorization.
2. `docs/ui-workflows.md` for required workflow, state and responsive order.
3. `.interface-design/system.md` for canonical tokens and shared visual rules.
4. `docs/ui-prototype/` for approved composition and component examples.
5. This plan and `docs/ui-visual-fidelity-coverage-matrix.md` for scope, order
   and acceptance evidence.
6. Historical `bodylife-light-v1` captures only for provenance and regression
   context. They are not the current layout target.

If a template contains fixture-only behavior, production keeps the existing
server behavior. A genuinely new product behavior requires its own contract
decision; it must not be inferred from prototype JavaScript.

## Migration objective and definition of done

Migrate every existing visual route, shared composition partial and workflow
partial to one current-Home production system without changing business truth.

Done means all of the following are true:

- all 15 visual page files / 16 visual route entries and all 21 partials in the
  coverage matrix are accounted for;
- the approved hierarchy, palette, typography, spacing, surfaces, semantic
  signals and responsive order are consistent across the application;
- every existing command/query, permission, antiforgery token, input name,
  `hx-*`, `data-busy-*`, stable target and canonical reread still works;
- every applicable role, culture, critical state and provenance state is
  represented by deterministic production evidence;
- tablet `1024x768` and phone `390x844` have no page-level horizontal overflow,
  hidden warning/action, keyboard trap or target smaller than 44x44;
- automated gates and independent review report zero P0/P1 findings;
- the product owner accepts each focal anchor and the final all-route gallery;
- legacy visual CSS and superseded assertions are removed only after their last
  consumer has migrated.

## Invariants that visual work must preserve

- Razor Pages/MVC remains server-rendered; htmx remains limited to the current
  reception islands. Prototype JavaScript and fixture state machines are not
  copied into production.
- Membership formulas, warnings, report totals and allowed actions remain
  server-owned. UI code never recalculates or optimistically invents them.
- State-changing workflows retain authorization, validation, idempotency,
  transaction/audit behavior, busy/disabled state and canonical reread.
- Search keeps exact/multiple/no-result/error behavior and the stable
  `#reception-search`, `#client-search`, `#search-loading`,
  `#reception-workspace`, `#client-profile` and `#profile-loading` contracts.
- On `/Reception/Index`, the route-local `#reception-search` htmx form owns the
  flexible header Search track. Other routes use the distinct
  `#global-client-search` ordinary GET fallback. They are compatible entry
  points but never render together or duplicate the Clients input.
- Direct Create Client remains permission-aware and reaches the existing
  CreateClient workflow without requiring a failed search.
- Cancel/correct workflows retain the visible original fact, reason,
  confirmation, stale/concurrency handling, changed-after-close and success
  relabeling. Backfill/paper origin and occurred-vs-recorded Kyiv time remain
  visible.
- Owner tools, shared Reception/Admin identity, device/session disclosure,
  language POST and logout POST remain truthful and server-owned.
- No database migration, domain command change or ADR change belongs to a
  visual-only wave. If a query/handler must change, it is reviewed and tested
  as an explicit behavioral sub-scope.

## Production styling architecture

The migration uses one canonical production design-system layer instead of
copying the prototype stylesheet over the application:

- canonical tokens use the names in `.interface-design/system.md`
  (`--desk-*`, `--text-*`, `--checkin-*`, `--attention-*`, `--stop-*`,
  `--owner-*`, `--type-*`, `--space-*`, `--radius-*`);
- new shared shell/component selectors are scoped and semantic; existing class
  names may remain temporarily as behavioral/test compatibility hooks;
- legacy `site.css` rules are removed from a deletion ledger only after all of
  their consumers migrate; no wave stacks another untracked page-local palette;
- `site.js` gains only the minimum accessible production behavior needed for
  drawer/account focus, Escape, inert/overlay and busy htmx reinitialization;
  fixture dialogs, role switches and demo filters do not ship;
- existing local logo and SVG icon sprite are reused; no runtime font or icon
  dependency is added.

Before each component implementation, record the Interface Design checkpoint:

| Decision | Current-Home contract |
| --- | --- |
| Intent | Busy reception/admin staff can find, understand and act in seconds. |
| Hierarchy | One focal task; Home = Activity, Clients = identity/warning/membership/actions/history. |
| Palette | Graphite primary; check-in blue; green success; amber review; red stop; violet Owner. |
| Depth | Canvas → paper/card → raised popover only. |
| Surfaces | Quiet warm-neutral desk, restrained borders, no card-in-card clutter. |
| Typography | Segoe UI Variable/system; 11/14/17/22/28; tabular operational numbers. |
| Spacing | 4px base; 10/16/18 radii; all operational controls at least 44px. |

## Scope map

| Product area | Template evidence | Production routes / composition | Primary test families |
| --- | --- | --- | --- |
| Shared/Home | `index.html` | `/`; `_Layout`, `_AppNavigation`, `_CurrentSession`, `_LanguageSelector`, `_Icon` | `ReceptionHomeSmokeTests`, `UiStyleCoverageSmokeTests`, localization |
| Clients/actions | `clients.html` and its deterministic states | `/Reception/Index`; 14 Reception partials | Reception dashboard plus Create/Update/Card/Visit/Payment/Freeze/Membership suites |
| Reports | five `reports-*.html` pages | five `/Reports/*` pages | five report smoke suites and consistency tests |
| Audit/history | `audit-timeline.html`, `client-history.html` | `/Audit/Timeline`, `/Audit/ClientHistory` | Audit Timeline and Client History suites |
| Owner | three `owner-*.html` pages | three `/Owner/*` pages; two NonWorkingDay workspaces | Membership catalog, NonWorkingDay and Staff Account suites |
| Public/status | `login.html`, `logged-out.html`, `access-denied.html`, `error.html` | `/Login`, `/Logout`, `/AccessDenied`, `/Error` | auth/public visual coverage to be added |

Static `state=` parameters are review fixtures, not production URLs. Production
evidence creates the equivalent state through its real seeded query/command
result or a deliberate typed failure.

## Executable waves

Every wave moves through `not started → in progress → candidate → approved`.
One writer owns one bounded wave. Automated success makes a candidate; only the
explicit product decision makes a focal anchor approved.

### Wave 0 — contract, inventory and baseline

Status: **completed on 2026-08-11**.

Outputs:

- record the current-Home template as the authorized production target and the
  earlier production composition as superseded;
- map all 16 template pages to production routes, 16 workflow partials, five
  shared partials, localization and existing tests;
- inventory stable selectors, htmx/form contracts, roles, states and provenance;
- capture the static target at `1024x768` and `390x844` and identify every
  hard-coded assertion tied to the rejected production layout;
- establish wave ownership, stop/go, rollback and acceptance rules.

Gate: plan and matrix independently reviewed; solution builds; no production
UI file changes. The current environment lacks
`BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING`, so a real Razor capture is a
mandatory Wave 1 prerequisite rather than simulated evidence.

### Wave 1 — canonical system, authenticated shell and Home

Status: **approved by the product owner on 2026-08-12 after the revised
desktop/tablet/phone composition replaced the rejected first render**.

Owned production surface:

- shared authenticated shell/navigation/session/language/icon composition;
- canonical production tokens and semantic shell/Home components;
- root Home composition: global Search, permission-aware direct Create Client,
  honest account menu, Activity focal surface, separate Attention and Today;
- accessible tablet drawer and phone single-column order;
- focused shell/Home localization and Playwright assertions for Owner, named
  Admin and shared Reception/Admin.

Behavior deliberately retained: existing Activity/Attention/Daily queries,
activity links, provenance, failures and Home query error semantics. No static
filter, demo banner, state links, fake client rows or fixture account switcher
ships to production.

Candidate files include `Pages/Shared/_Layout.cshtml`,
`_AppNavigation.cshtml`, `_CurrentSession.cshtml`, `_LanguageSelector.cshtml`,
root `Pages/Index.cshtml`, shared CSS/JS, localization resources and focused UI
tests. A new shared partial is allowed only when it eliminates duplication and
keeps permission/session rendering server-owned.

Wave 1 changes only the authenticated branch of `_Layout.cshtml`. Its public
Login/AccessDenied/Error composition is structurally deferred to Wave 5; run
tablet/phone no-regression renders for that untouched branch whenever shared
CSS or script loading changes.

Candidate evidence:

- the authenticated Razor shell now uses the exact 240px labeled desktop rail,
  88px header, intrinsic tablet wordmark/flexible Search composition and compact
  phone rows from the current-Home contract;
- the thin 430/500-weight hierarchy, 104px icon-only rail, cooler parallel
  palette and flat gray Activity rows from the rejected first render were
  removed; Activity now uses bounded paper rows and restrained semantic rails;
- populated Home is exercised for Owner, named Admin and shared
  Reception/Admin in `uk-UA` and `en-US` at `1024x768` and `390x844`;
- a separate PostgreSQL-backed matrix exercises honest Activity empty, Activity
  unavailable, Attention unavailable and Today unavailable branches for the
  same actor/culture/viewport matrix;
- drawer focus containment/return, account Escape, overlay close, exact and
  location navigation, unique Search ids, direct Create, ordinary GET fallback,
  44px targets, rendered AA contrast and no horizontal overflow are automated;
- the complete authenticated Playwright regression suite passes 141/141 after
  the revised shell, role labels, skip-link focus behavior, AA tertiary text
  and all legacy route consumers were revalidated;
- revised candidate captures are under
  `/tmp/bodylife-production-wave1-refined-v2/`; the populated anchors are
  `wave1-home-desktop-1440x900-uk.png`,
  `wave1-home-tablet-1024x768-uk.png` and
  `wave1-home-phone-390x844-uk.png`;
- independent closure review reports zero remaining P0–P3 findings.

Stop/go:

- build, focused query tests and production Playwright pass with PostgreSQL;
- global/reception search ids are unique; fallback navigation and direct Create
  reach the existing server workflow;
- drawer, account menu, language and logout pass keyboard/focus checks;
- authenticated navigation asserts exact Home and Clients states, exact and
  section `/Reports/*` states, exact Timeline and Client History location state,
  plus Owner-tool visibility/denial across the applicable actors;
- populated/empty and independently unavailable Activity, Attention and Today
  states render honestly for all applicable actors and both cultures at
  `1024x768` and `390x844`;
- desktop `1440x900` asserts the 240px labeled rail; tablet protects the
  8–20px brand-to-Search gap and at least 300px flexible Search width; phone
  protects brand/account → Search → Create ordering and visible role text;
- no P0/P1 independent-review finding;
- product owner approved the real Home desktop/tablet/phone candidate on
  2026-08-12, opening Wave 2. Any later shell regression reopens only Wave 1.

### Wave 2 — Reception Search and Create Client

Status: **approved by the product owner on 2026-08-12 after the flat-ledger
replacement; automated gates and independent correctness/design reviews are
green**.

Migrate `/Reception/Index`, `_ReceptionWorkspace` and `_CreateClientForm` to
the approved shell/components. Cover idle/loading, exact, multiple, no-result,
query failure, direct open, validation, duplicate warning/acknowledgement,
permission, busy/duplicate submit and success canonical reread. Keep the global
GET search on other routes and the reception htmx search on Clients as two
compatible, mutually exclusive header entry points.

Gate: all search/create states and roles at tablet/phone, stable selectors and
fallback hrefs intact, zero P0/P1, explicit Search/Create anchor approval.

Current replacement candidate evidence:

- the product owner rejected the first Wave 2 render because it capped the
  header Search and fragmented Clients into separate Search, Results, Create
  and empty Profile surfaces. That candidate and its `v3` captures are
  superseded, not acceptance evidence;
- `/Reception/Index` now renders exactly one visible Search: the real
  `#reception-search` htmx form fills the shared header track beside Create.
  Non-Clients routes retain the distinct ordinary GET global Search;
- the product owner also rejected the later large `#reception-workspace`
  Clients canvas because it placed rounded result cards inside another raised
  card and its shortened blue row accents did not follow the rounded corners;
- `#reception-workspace` and the Results region are now transparent htmx state
  outlets. Idle is a quiet text cue; non-empty results use a lightweight
  heading/count plus independent compact rounded paper bands. Every result
  owns a real 4px blue inline-start border that follows the radius. Create and
  selected Profile each own one raised surface only while active;
- direct Create keeps the existing permission-aware workflow and lands at
  `#create-client-action-panel`; validation, duplicate acknowledgement/reason,
  busy/idempotency and canonical reread are unchanged;
- both header Search and result-to-Profile htmx swaps return the active state below
  the sticky header, including after the operator has scrolled deep into a
  long profile;
- real viewport captures are under `/tmp/bodylife-wave2-flat-ledger-final-v6/`
  for `1440x900`, `1024x768` and `390x844`; independent interface review
  reports zero remaining P0–P3 findings;
- Release build passed with zero warnings/errors, the focused responsive
  Search/Profile path passed 3/3, Home compatibility passed 4/4, and the full
  authenticated Playwright regression suite passed 144/144.

Stop/go:

- present the real desktop/tablet/phone Search/Create anchor and live isolated
  instance to the product owner;
- rejection reopens only Wave 2; approval opens Wave 3 without changing any
  server contract or accepting the still-unmigrated profile/action visuals.
- the product owner explicitly approved the flat result-ledger replacement on
  2026-08-12, opening Wave 3. Any later Search/Create regression reopens only
  Wave 2.

### Wave 3 — Client profile and all Reception actions

Status: **candidate completed on 2026-08-12; automated gates and independent
review are green; explicit Profile and Cancel Visit visual approval remains
pending**.

Migrate `_ClientProfile` and the remaining 12 Reception action partials in
small related slices: identity/membership/warnings; visit and Cancel Visit;
membership issue/negative coverage/issued-sale correction; payment; freeze;
client/card update. Preserve active/zero/negative/expired/ending/low/inactive,
permission, validation, busy, stale/concurrency, backfill/fallback and success
states. ADR-018 exact payment, oldest-first negative coverage and sale
replace/cancel stay explicit.

Gate: focused command/PostgreSQL/UI tests per slice, no hidden warning/action,
canonical reread after every mutation, explicit Profile and Cancel Visit
tablet/phone approval.

Current candidate evidence:

- the real server-rendered Profile is one raised paper surface ordered as
  identity/status → warnings → membership readiness → risk contexts → actions
  → visit/payment ledger → records → quiet identity/card management;
- one blue rail now belongs only to the outer Profile paper. Neutral identity
  and note, blue membership and graphite actions use tonal fills/dividers,
  never competing inner left rails. Amber/red remain reserved for
  review/danger contexts;
- the membership readiness strip exposes the human membership name, status,
  signed remaining visits and effective end date with semantic text-backed
  signals. The server still reads the immutable issue-time snapshot, but the
  Reception UI no longer exposes `snapshot`/`знімок` persistence vocabulary;
  zero-safe negative coverage is omitted by a server-owned presentation
  decision, while query failure, concrete negative balance, active closure and
  correction/error states remain visible;
- the reception action workstation exposes Mark Visit, Issue Membership, Add
  Payment and Add Freeze before a single full-width active server form. Mark
  Visit is initial; the JavaScript switcher is presentation-only and preserves
  native `details` no-JavaScript fallback, stable partial ids, htmx swap
  targets, busy/idempotency and canonical rereads;
- Cancel Visit, Correct Payment and Cancel Freeze remain contextual to their
  source rows. Negative coverage and issued-sale correction remain explicit
  danger/review contexts rather than ordinary quick actions;
- a successful canonical mutation now renders a compact inset status row in
  normal flow below identity instead of a full-width highlighted region;
- the activity ledger is split into independent Recent Visits and Recent
  Payments groups. Desktop uses two columns without stretching an empty group;
  tablet/phone stack the groups. Each source event is a flat readable row and
  its cancellation/correction remains nested inside that row;
- the shared rail now has explicit Operations, Records & history and
  role-gated Owner tools groups rather than one flat list/More disclosure;
- corrected Profile/status captures are under
  `/tmp/bodylife-wave3-ledger-redesign-root-v6/` at `1440x900`, `1024x768`
  and `390x844`; long activity-ledger captures are under
  `/tmp/bodylife-wave3-ledger-redesign-root-v4/`;
- Release build passed with zero warnings/errors, Web tests passed 378/378,
  focused responsive Profile and no-JavaScript coverage passed 4/4, focused
  action workflows passed 24/24 and the final complete authenticated
  Playwright suite passed 146/146. The independent redesign review found no
  remaining P0–P3 after its note-rail and coverage observations were closed.

Stop/go:

- present Profile and Cancel Visit desktop/tablet/phone evidence in the single
  live instance;
- rejection reopens only Wave 3; explicit acceptance opens Wave 4 without
  changing any command/domain/persistence contract.

### Wave 4 — Owner tools

Status: **not started; blocked by Wave 3 approval**.

Migrate Membership Types, Non-Working Days and Staff Accounts plus both
NonWorkingDay workspaces. Cover Owner visibility, Admin denial, empty/list,
validation, activation/deactivation, credentials, preview/confirmation,
expired token, changed scope and correction/cancel.

Gate: permission and confirmation contracts unchanged; focused suites pass;
Owner gallery approved at desktop/tablet and phone.

### Wave 5 — Reports, Audit, Client History and public/status pages

Status: **not started; blocked by Wave 4 approval**.

Migrate the five reports, Audit Timeline, Client History and Login/Logout/
AccessDenied/Error. Retain corrections/cancellations, totals-to-drilldown truth,
changed-after-close, negative coverage, source identifiers, paper batch/sheet/
row/event provenance and Kyiv time. Empty/unavailable never becomes fake zero.
Add missing dedicated auth/status visual tests.

Gate: report/audit consistency and route UI suites pass; both cultures,
representative actors, empty/error/provenance states and phone layouts covered;
route gallery approved.

### Wave 6 — legacy CSS retirement and all-route acceptance

Status: **not started; blocked by Wave 5 approval**.

Remove only selectors proven dead by the migration ledger, consolidate the
canonical stylesheet, remove compatibility hooks and superseded visual
assertions, then run the full repository gate and full production route/state
gallery. No structural redesign is allowed in this cleanup wave.

Gate: all matrix rows have evidence, hashes/harness are reproducible, full test
suite passes or exact external blockers are recorded, zero P0/P1 remains, and
the product owner signs off the final gallery.

## Deterministic verification contract

For every changed route/partial:

1. Use deterministic PostgreSQL seed, fixed Kyiv business time, actor, culture
   and viewport. Never use prototype fixtures as production evidence.
2. Verify DOM/ARIA order, unique ids, stable forms/hx/data selectors, normal
   fallback navigation and canonical result text.
3. Verify pointer and keyboard behavior, visible focus, busy/disabled state,
   44x44 targets, reduced motion and no page-level horizontal overflow.
4. Verify semantic color with a text/icon cue and WCAG 2.2 AA contrast.
5. Capture named tablet and phone artifacts with volatile session/id/time
   suffixes masked, not removed from the visible structure.
6. Run focused behavior tests, independent read-only review and a side-by-side
   product review for focal anchors.

Severity:

- **P0:** broken/unauthorized workflow, wrong canonical state, lost htmx/form
  contract, hidden critical warning/action/correction or blocked overflow.
- **P1:** material composition/hierarchy/token mismatch, fake/missing canonical
  block, focus/keyboard/contrast failure or inconsistent shared shell.
- **P2:** local cosmetic difference that preserves meaning, reachability and
  accessibility; defer only with an explicit recorded decision.

## Change and rollback discipline

- One logical commit per independently reversible wave or workflow slice;
  implementation, focused tests and directly related docs travel together.
- Do not mix UI migration with Milestone 11, unrelated refactors, package files,
  database changes or user worktree changes.
- If a wave fails its gate, revert or correct only that unapproved wave. The
  last approved functional state remains deployable.
- Update this plan, the coverage matrix and `implementation-progress.md` at
  every candidate/approval boundary. Automated tests never silently advance an
  approval state.

## Current next action

Present the real Wave 3 Profile/actions candidate from
`/tmp/bodylife-wave3-profile-root-v5/` and
`/tmp/bodylife-wave3-profile-actions-v3/` in the single live review instance.
Obtain explicit Profile and Cancel Visit approval before starting Wave 4.
