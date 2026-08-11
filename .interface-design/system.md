# BodyLife interface system — current-Home baseline

## Decision checkpoint

**Intent.** Busy reception and admin staff search, open and act in seconds. The
product should feel like a calm, bright physical check-in desk, never a generic
SaaS dashboard.

**Hierarchy.** Each view names one focal task. Home keeps Activity focal;
global Search and direct Create Client live in the header; Attention and Today
are context. Clients prioritise identity → critical warning → membership signal
→ actions → history. Reports, history and Owner tools state their local focal
task in the page heading.

**Palette and depth.** The colour world is a warm-neutral/blue-gray desk:
graphite for the one safe primary action, check-in blue for navigation and
information, green for confirmed state, amber for review, red for stop or
destruction and violet for Owner/restricted context. Colour is paired with
text/icon labels. Raised cards and popovers use the single quiet elevation
strategy; translucent lines structure paper surfaces and inset controls are
slightly darker than paper.

**Surfaces, type and spacing.** Canvas → paper/card → raised popover is the
only elevation ladder. Use `Segoe UI Variable`, `Segoe UI`, system fallback;
the 11/14/17/22/28px scale, tabular numbers and a weight-led hierarchy:
800–850 for the wordmark/page/focal names, 700–750 for actions and event
labels, and 400 for ordinary copy. Tertiary text must still meet WCAG AA on
paper; production currently uses the accessible `#697780` correction.
Spacing is a 4px base; controls are at least 44px. Radii are control 10px,
card 16px and popover 18px.

## Tokens and actions

Canonical CSS tokens are `--desk-canvas`, `--desk-paper`, `--desk-inset`,
`--text-primary`, `--text-secondary`, `--text-tertiary`, `--line-quiet`,
`--checkin-blue`, `--checkin-green`, `--attention-amber`, `--stop-red`,
`--owner-violet`, `--focus-ring`, `--elevation-card`, `--elevation-popover`,
`--type-*`, `--space-*` and `--radius-*`. Compatibility aliases may point to
these tokens, but page-local or `fixture` token systems must not be introduced.

Action hierarchy is universal: near-black is the single current safe primary;
blue is navigation/selection/info; neutral is secondary; red is correction or
destruction; violet is Owner/restricted. Card, input, button, focus, hover,
active, disabled, busy and reduced-motion states use this system.

## Shared composition and responsive order

Authenticated desktop pages at `>=1100px` use a 240px labeled rail below an
88px sticky header with logo, global Search, direct Create Client and an honest
account menu. Tablet uses a drawer; its intrinsic wordmark is followed directly
by the flexible Search track. Phone orders brand/account, Search and Create on
three compact rows, then becomes one content column. The account role remains
text-visible at 390px and the device/session detail remains in the menu.
Review-only controls, demo banner, fixture state links and fixture role switch
belong only to the static lab and do not ship in production. The `check-in
signal` is a semantic left rail/status strip on Activity,
search/results/profile context, report/history/audit rows and important action
contexts.

## State requirements

Templates display server-owned/canonical results only; previews are deterministic
and nonpersistent. Required command states retain permission, busy/idempotency,
validation, stale/concurrency, success and canonical-reread meaning. Create
Client is directly reachable without a failed search. Cancel Visit preserves
the original visible fact and labels it canceled after success. Kyiv time,
paper origin, cancellation/correction and ADR-018 sale/negative-coverage
context remain visible.

## Baseline note

`docs/ui-prototype/index.html` and the complete `docs/ui-prototype/` inventory
are the approved evolved composition baseline for the production Razor
migration authorized on 2026-08-11. Locked references remain immutable
historical/audit inputs only; the earlier production sidebar/top-context/Quick
Search candidate and the first thin-type/icon-rail Wave 1 render are functional
history, not the visual target. Each real Razor anchor still requires
PostgreSQL-backed desktop/tablet/phone evidence and explicit product-owner
acceptance before the following migration wave begins.
