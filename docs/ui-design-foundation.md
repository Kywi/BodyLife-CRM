# BodyLife CRM UI design foundation

Дата: 2026-07-09
Статус: design foundation for v1 implementation

Цей документ задає мінімальну design-system основу для BodyLife CRM v1. Він доповнює `docs/ui-workflows.md`: workflows описують, які screen/state/actions потрібні, а цей документ описує, як сторінки мають виглядати, повторюватися і поводитися на tablet/phone без хаотичної імпровізації.

Це не pixel-perfect mockup і не дозвіл робити декоративний redesign. Accepted ADR package у `docs/adr/` лишається вищим джерелом правди. Якщо цей документ конфліктує з ADR, перемагає ADR.

## 1. Product posture

BodyLife CRM має виглядати як спокійний внутрішній operational tool для рецепції залу, а не як landing page, SaaS marketing site або generic admin CRUD.

Дизайн має оптимізувати:

- швидкий пошук клієнта;
- миттєве розуміння current membership state;
- видимість warnings перед ризиковими діями;
- безпечні quick actions на touch device;
- чесну session/accountability інформацію;
- однакові патерни для profile, reports, history and correction flows.

Візуальна якість означає scanability, predictable layout, clear hierarchy and low mistake rate. Декор, великі hero sections, градієнтні фони, illustrative marketing blocks and generic table-first CRUD не є ціллю v1.

## 2. Source hierarchy

UI implementation має читати документи в такому порядку:

1. Accepted ADR package, especially ADR-003, ADR-008, ADR-012 and ADR-013.
2. `docs/architecture-baseline.md` for implementation guardrails.
3. `docs/ui-workflows.md` for workflow behavior and required states.
4. This document for visual, layout and component consistency.
5. `docs/interaction-contracts.md` for command/query errors, rereads and authorization behavior.

Якщо screen потребує нового product behavior, спочатку оновити workflow/contract docs. Якщо потрібна зміна accepted architecture direction, потрібен ADR update.

## 3. Information hierarchy

Reception screens should prioritize information in this order:

1. Search and selected client identity.
2. Critical warnings that can change the meaning of the next action.
3. Current membership state and visit/payment readiness.
4. Primary quick actions allowed by server permissions.
5. Recent history, daily report context and audit/history links.
6. Secondary admin/settings actions.

Critical warnings must not be visually weaker than ordinary metadata. A compact layout may shorten labels, but it must not hide negative, zero, expired, duplicate, stale, changed-after-close, backfill/fallback or permission warnings behind an extra tap.

## 4. Layout model

Use a stable application shell:

- Top bar: app name or current area, current account/session/device indicator, current business date when relevant, and owner/admin context when present.
- Main reception area: search/result area plus selected client/profile area.
- Secondary area: daily report link/summary, recent history, audit/history links and non-primary actions.

Tablet is the primary reception target:

- Use a two-area layout when space allows: search/results on the left, selected client/profile on the right.
- Keep the active client, warnings and primary actions in the first visible viewport.
- Avoid wide empty marketing-like space; density should help repeated operational use.

Phone layout must become one readable column:

1. Search input and active search status.
2. Selected client identity or compact result list.
3. Critical warnings.
4. Membership state.
5. Primary quick actions.
6. Recent history and report/audit links.

Desktop may add breathing room, but it must not introduce a different workflow model. Do not design desktop as the canonical layout if tablet and phone become weaker.

## 5. Visual language

Use a neutral operational base with semantic color accents:

- Background: light neutral gray.
- Surfaces: white or very light neutral.
- Text: high-contrast dark neutral, muted neutral for secondary metadata.
- Border/divider: visible but quiet neutral.
- Focus/action: clear blue accent.
- Success/active: green.
- Warning/ending/low/zero: amber.
- Danger/expired/negative/destructive: red.
- Info/stale/changed-after-close/backfill/fallback: cyan or blue.
- Owner-only/admin-sensitive: restrained violet accent, used sparingly.

Do not make the UI dominated by one hue family. Avoid heavy purple/blue gradients, beige/brown themes, dark dashboard styling, decorative orbs, bokeh blobs, or large illustrative backgrounds.

Status color is never the only signal. Pair color with icon/label/text such as `Active`, `Zero visits`, `Negative`, `Expired`, `Owner only`, `Changed after close`.

## 6. Spacing, shape and typography

Use a compact, predictable scale:

- Base spacing: 4/8/12/16/24/32 px.
- Cards and panels: border radius 8 px or less.
- Touch targets: at least 44x44 px; primary form submits should feel comfortable on tablet.
- Inputs and buttons: stable height so loading, validation and long labels do not shift layout.
- Tables/lists: dense enough for scanning, with enough row height for touch.

Typography:

- Use system UI fonts unless a future design decision says otherwise.
- Do not scale font sizes with viewport width.
- Do not use negative letter spacing.
- Use large type only for true page-level context, not inside compact cards/panels.
- Align numeric values such as visits, money and dates for easy comparison.

## 7. Core components

Build these as reusable Razor partials/view components or consistent CSS patterns before broad page work.

### App shell

Shows the current area, account type, account/session/device metadata and safe navigation to dashboard/report/history areas. Shared Reception/Admin sessions must be visually honest; do not imply a physical person if the system only knows the shared account.

### Search island

Contains one primary search input that supports card/name/phone/last4 behavior from `SearchClients`. Optional mode controls should be compact and secondary; exact card search remains the fast path. Search loading and stale-response handling must be visible.

### Search result row

Each result row shows enough context to choose safely without opening generic edit screens:

- display name;
- phone display;
- current card or no-card marker;
- operational status;
- match type;
- current membership summary from server reads;
- row-level warnings.

Rows must be touch-selectable and keep warnings visible on phone.

### Client identity header

Shows client name, current card, phone, operational status and duplicate/inactive markers. It should fit above membership state without feeling like a marketing profile card.

### Membership status panel

Shows server-provided membership state only:

- type snapshot/name;
- start/base/effective dates;
- counted visits and remaining visits;
- negative balance and first negative visit date when present;
- extension days/explanation;
- last counted visit;
- warnings.

The panel may emphasize one main readiness status, but it must still show the underlying server fields needed for reception trust.

### Warning block

Warnings use a consistent block/chip pattern with severity:

- `info`: stale state, changed-after-close, backfill/fallback label;
- `warning`: ending soon, low remaining, zero visits, duplicate-looking identity;
- `danger`: expired, negative balance, blocking validation;
- `restricted`: permission/owner-only action.

Blocking warnings that require acknowledgement must sit directly above the affected submit action and include the acknowledgement control in the same form context.

### Quick action group

Primary actions are visible and permission-aware:

- Mark visit;
- Issue membership;
- Add payment;
- Add freeze;
- Open daily report;
- Open history/audit.

There should be one visually dominant action per local context. Destructive/correction actions use a restrained danger style and always show reason/comment requirements.

### Action form

Forms use consistent field order:

1. Affected client/membership summary.
2. Required business inputs.
3. Server-provided warnings.
4. Acknowledgement, confirmation or reason/comment when required.
5. Submit/cancel actions.
6. Inline validation/error result.

State-changing submits must disable and show busy state after tap/click. Duplicate submission outcomes must render as business-safe repeat outcomes, not as mystery errors.

### History and report rows

History/report rows show source facts and correction/cancellation state without hiding the original record. Totals belong to report queries; rows should link to profile/history/audit drill-downs where available.

## 8. htmx interaction pattern

Use htmx only for reception-critical islands:

- live/quick search;
- compact result replacement;
- selected client/profile refresh;
- membership state refresh;
- warning/action form replacement;
- daily report drill-down replacement;
- loading and duplicate-submit protection.

Each htmx island needs:

- a stable `id`/target;
- visible loading state;
- stale-response guard where multiple requests can race;
- server-rendered error state in the same context;
- canonical reread after successful mutation.

Do not leave locally calculated or optimistic membership values in the DOM after state-changing commands. After success, rerender the relevant profile/membership/report fragment from canonical server queries.

## 9. Button and command states

Buttons should communicate command risk:

- Primary: safe high-frequency action for the current context, usually `Mark visit` or `Open client`.
- Secondary: useful but less frequent actions such as `Add payment`, `Add freeze`, `Open report`.
- Danger: correction/cancellation/destructive action with confirmation and reason/comment.
- Restricted/disabled: unavailable action with server-provided reason when helpful.

Busy state must:

- start immediately on submit/tap;
- prevent duplicate taps;
- survive slow server response;
- restore or rerender from server response;
- not hide validation errors.

## 10. Empty, error and stale states

Empty states should be operational, not promotional:

- no selected client: focus search and show daily report access;
- no search results: offer name/phone/card refinement;
- no active membership: show allowed next actions and warnings;
- no recent history: say there are no source rows yet.

Errors render near the action that caused them. `stale_state` and `concurrency_conflict` states should ask for refresh and keep the previous canonical state visible until the reread succeeds.

## 11. First screens to implement as visual exemplars

Before building broad UI, create and verify these three exemplars:

1. Reception dashboard: empty/search, exact card auto-open, multiple results and no-match states.
2. Client profile shell: identity, membership panel placeholder/current state, warnings, quick actions and recent history.
3. Risky action form: mark visit through zero/negative warning acknowledgement with busy/disabled submit and canonical profile reread.

These exemplars become the reference for later payment, freeze, correction, report and audit screens.

## 12. Implementation notes

- Prefer shared CSS variables/tokens and small reusable Razor components over ad hoc page-local styling.
- Keep component names aligned with workflow roles: search island, result row, membership panel, warning block, quick action group, action form, report row.
- Use icons in buttons or status chips when they clarify action/status, but text labels must remain clear.
- Do not put cards inside cards. Use cards for repeated result/history/report rows, modals and genuinely framed panels; page sections should be shell regions or full-width bands.
- Do not add UI formulas for membership state to templates, JavaScript, controllers or report views.
- Do not hide server permission policy behind client-only disabled UI; every command still rechecks server-side.

## 13. Acceptance checklist

- Reception dashboard is the first real screen, not a landing page and not generic CRUD.
- Tablet layout keeps selected client, warnings, membership state and quick actions visible.
- Phone layout preserves every critical warning/action in a single usable order.
- Search result rows are compact but distinguish clients safely.
- Membership panel uses server-provided state and does not calculate formulas locally.
- Warning blocks use consistent severity semantics and stay visible after canonical rereads.
- State-changing buttons show busy/disabled state and prevent duplicate submits.
- Destructive/correction actions require confirmation plus reason/comment when contracts require it.
- Owner/shared account/session context is visible and honest.
- Playwright smoke checks cover tablet and phone viewport rendering for the exemplar screens.
