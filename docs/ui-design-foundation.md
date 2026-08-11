# BodyLife CRM UI design foundation

Дата: 2026-07-09
Оновлено: 2026-08-11
Статус: accepted current-Home production migration target; real Razor wave acceptance remains evidence-gated

Цей документ задає мінімальну design-system основу для BodyLife CRM v1. Він доповнює `docs/ui-workflows.md`: workflows описують, які screen/state/actions потрібні, а цей документ описує, як сторінки мають виглядати, повторюватися і поводитися на tablet/phone без хаотичної імпровізації.

Погоджений evolved Home у `docs/ui-prototype/index.html` і повний inventory
`docs/ui-prototype/` задають binding composition для production Razor
міграції, авторизованої 2026-08-11. Locked reference package лишається
immutable historical/audit provenance, а не вимогою повернути відхилену ранню
production композицію. Pixel-diff не замінює workflow, accessibility або
explicit acceptance кожної real Razor wave. Accepted ADR package у
`docs/adr/` лишається вищим джерелом правди; якщо цей документ конфліктує з
ADR, перемагає ADR.

## Current functional baseline and accepted target

Поточна реалізація має функціональну та behavioral validation, але user не
прийняв її як visual-fidelity реалізацію. Вона не є візуальним authority.
`docs/ui-prototype/index.html` визначає approved evolved composition для
production migration. Locked reference package та його repository copy
залишаються immutable historical/audit provenance, а не вимогою повернути стару
композицію. Production waves і explicit visual approval визначає
`docs/ui-visual-fidelity-migration-plan.md`; static lab є design authority, але
не доказом коректності майбутнього Razor rendering.

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

1. Accepted ADR package, especially ADR-003, ADR-008, ADR-012, ADR-013 and
   ADR-017, plus `docs/architecture-baseline.md` for guardrails.
2. Domain/data/interaction contracts for behavior, ownership, commands,
   queries, errors, authorization, time and canonical rereads.
3. `docs/ui-workflows.md` for workflow behavior and required states.
4. Approved evolved `docs/ui-prototype/` composition for production template
   migration; locked references only for historical audit provenance.
5. `docs/ui-visual-fidelity-migration-plan.md` and its coverage matrix for
   ordering, exact scope and acceptance.
6. This document for shared semantic/accessibility constraints.

Якщо screen потребує нового product behavior, спочатку оновити workflow/contract docs. Якщо потрібна зміна accepted architecture direction, потрібен ADR update.

## 3. Information hierarchy

Reception Home keeps `Activity now` focal; global header Search and direct
`Create Client` are immediate entry points, while `Attention` and `Today` are
context. Clients/Profile follows: selected
identity, critical warnings, Membership state, allowed quick actions, Context
and recent history. Secondary reports, audit and Owner tools remain reachable
without crowding the primary four-item navigation.

Critical warnings must not be visually weaker than ordinary metadata. A compact layout may shorten labels, but it must not hide negative, zero, expired, duplicate, stale, changed-after-close, backfill/fallback or permission warnings behind an extra tap.

## 4. Layout model

Use the approved current-Home shell:

- Desktop uses the approved 240px labeled navigation rail below an 88px sticky
  header with logo, global Search, direct Create Client and honest account
  menu; tablet uses the drawer with an intrinsic brand track and flexible
  Search immediately after it.
- Workspace context carries current area, Kyiv date and accountable account;
  device/session remains available from the menu.
- Reception Home: wide Activity card plus narrow Attention/Today context rail.
- Client/Profile: main identity/Membership/history column plus Quick
  actions/Context rail.
- Report/Audit/Owner pages retain the same shell and use local secondary
  navigation for routes omitted from the compact primary rail.

Tablet is the primary reception target:

- Use the current-Home two-area proportions when space allows; Home and Profile
  have different content compositions but the same shell rhythm.
- Keep the active client, warnings and primary actions in the first visible viewport.
- Avoid wide empty marketing-like space; density should help repeated operational use.

Phone layout must become one readable column:

1. Compact brand/navigation and workspace/session context.
2. Home Activity or selected client identity.
3. Critical warnings and Membership state where a client is selected.
4. Quick Search or permission-aware primary actions.
5. Today summary or Context.
6. Recent history and report/audit links.

Desktop may add breathing room, but it must not introduce a different workflow model. Do not design desktop as the canonical layout if tablet and phone become weaker.

## 5. Visual language

Use a neutral operational base with semantic color accents:

- Background: light neutral gray.
- Surfaces: white or very light neutral.
- Text: high-contrast dark neutral, muted neutral for secondary metadata.
- Border/divider: visible but quiet neutral.
- Primary CTA: near-black current-Home treatment for the one dominant action.
- Navigation/selection/focus/info: clear blue accent.
- Success/active: green.
- Warning/ending/low/zero: amber.
- Danger/expired/negative/destructive: red.
- Info/stale/changed-after-close/backfill/fallback: cyan or blue.
- Owner-only/admin-sensitive: restrained violet accent, used sparingly.

Do not make the UI dominated by one hue family. Avoid heavy purple/blue gradients, beige/brown themes, dark dashboard styling, decorative orbs, bokeh blobs, or large illustrative backgrounds.

Status color is never the only signal. Pair color with icon/label/text such as `Active`, `Zero visits`, `Negative`, `Expired`, `Owner only`, `Changed after close`.

## 6. Spacing, shape and typography

Use the current-Home product tokens: warm-neutral/blue-gray canvas, graphite
primary, check-in blue navigation/info, green success, amber review, red stop,
violet Owner. Use 4px spacing, 10/16/18px control/card/popover radii, quiet
translucent borders and one subtle-shadow elevation system. These templates
must not create a parallel page-local palette.

Invariant constraints:

- Touch targets: at least 44x44 px; primary form submits should feel comfortable on tablet.
- Inputs and buttons: stable height so loading, validation and long labels do not shift layout.
- Tables/lists: dense enough for scanning, with enough row height for touch.

Typography:

- Use `Segoe UI Variable`, `Segoe UI`, then the local system fallback; do not
  add an external runtime font dependency.
- Use the 11/14/17/22/28px scale with 800–850 weight for the wordmark,
  page/focal headings and client names, 700–750 for operational actions/event
  labels and 400 for ordinary copy. Small tertiary text must remain AA-safe on
  paper; do not preserve a lighter prototype value at the cost of contrast.
- Do not scale font sizes with viewport width.
- Keep body copy at normal tracking; only page/panel headings may use the
  restrained current-Home optical tightening recorded in the token system.
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

## 11. Editable-lab composition anchors

Use these review anchors to preserve the current-Home composition while pages
are migrated:

1. Reception Home desktop and phone.
2. Client search/results desktop.
3. Direct Create Client desktop.
4. Client Profile desktop and phone.
5. expanded Cancel Visit desktop and phone.

The former eight locked reference files and hashes remain recorded in
`docs/ui-visual-fidelity-migration-plan.md` as historical delivery provenance.
They may explain earlier decisions, but they are not an exact composition or
pixel target for the editable lab. Production Razor acceptance remains a
separate route/state gallery with explicit product-owner approval.

## 12. Implementation notes

- Prefer shared CSS variables/tokens and small reusable Razor components over ad hoc page-local styling.
- Keep component names aligned with workflow roles: search island, result row, membership panel, warning block, quick action group, action form, report row.
- Use icons in buttons or status chips when they clarify action/status, but text labels must remain clear.
- Do not put cards inside cards. Use cards for repeated result/history/report rows, modals and genuinely framed panels; page sections should be shell regions or full-width bands.
- Do not add UI formulas for membership state to templates, JavaScript, controllers or report views.
- Do not hide server permission policy behind client-only disabled UI; every command still rechecks server-side.

## 13. Historical functional light baseline — not the target

The following records what exists before the fidelity migration so it can be
removed or retained deliberately. It is functionally validated but visually
rejected; none of these shell/layout/token choices is canonical target input.

- Authenticated shell використовує постійну груповану навігацію `Main` і
  Owner-only `Owner tools`, чесний account/session/device context та локальний
  прозорий BodyLife logo. На phone sidebar стає двоколонковою верхньою
  навігацією без втрати дій.
- Public shell для Login, Logout, AccessDenied і Error використовує той самий
  логотип, мову, focus treatment і світлу палітру.
- Current tokens у `site.css`: background `#f5f6f7`, surface `#ffffff`, text
  `#17211c`, muted `#5f6b66`, line `#d9dee3`, primary `#0a74c9`, success
  `#277a46`, warning `#9a5b0a`, danger `#b42318`, restricted `#6941c6`.
  Кожен semantic color має світлий companion surface і текстовий/icon label.
- Іконки постачаються локальним SVG sprite з MIT attribution; жодної CDN або
  runtime-залежності від зовнішнього icon provider немає.
- Reception direct create доступний незалежно від пошуку для дозволених
  Admin/Owner actors. Панель початково згорнута, вручну відкривається,
  автоматично відкривається після успішного zero-result search, зберігає
  submitted validation/duplicate-review state і згортається після canonical
  successful reread. Лише Card search prefill-ить card field.
- Cancel Visit, payment/freeze corrections, MembershipType deactivation і
  NonWorkingDay cancellation використовують danger treatment; preview,
  acknowledgement і confirmed outcomes залишаються окремими станами.
- Reports і Audit не змінюють source truth: semantic rails доповнюють, але не
  замінюють correction/cancellation, origin, changed-after-close і canonical
  row labels. Sidebar `Reports` позначає exact Daily route як current page, а
  інші report routes як current location/section.
- Reception workspace і Client History fixed filter grid переходять до
  компактнішого layout на `1100px`, щоб у primary `1024x768` tablet viewport
  врахувати ширину sidebar. Phone target `390x844` зберігає одну читабельну
  operational column.

Повний route/partial/test inventory і functional evidence записані в
`docs/ui-style-migration-inventory.md`.

## 14. Acceptance checklist

- Reception dashboard is the first real screen, not a landing page and not generic CRUD.
- Tablet layout keeps selected client, warnings, membership state and quick actions visible.
- Phone layout preserves every critical warning/action in a single usable order.
- Search result rows are compact but distinguish clients safely.
- Membership panel uses server-provided state and does not calculate formulas locally.
- Warning blocks use consistent severity semantics and stay visible after canonical rereads.
- State-changing buttons show busy/disabled state and prevent duplicate submits.
- Destructive/correction actions require confirmation plus reason/comment when contracts require it.
- Owner/shared account/session context is visible and honest.
- Historical locked references, manifest hashes and deterministic capture
  metadata remain unchanged for audit provenance; current-Home composition is
  the authorized production migration target, while the static lab itself is
  not acceptance evidence.
- Every route and partial row in the visual-fidelity matrix has evidence;
  anchors and the final gallery have explicit user/product-owner approval.
- WCAG AA contrast, visible keyboard focus, 44x44 targets and zero blocking
  overflow pass on tablet and phone.
- Green behavioral tests are required but cannot by themselves approve visual
  fidelity.
