# BodyLife CRM light UI migration inventory

Date: 2026-07-22

Status: functional UI migration implemented and behaviorally validated; visual fidelity rejected/pending

Architecture impact: none; post-Milestone 10 presentation hardening

## Scope

The recorded migration applied one light, branded, semantic visual system to
every v1 visual route while retaining ASP.NET Core Razor Pages/MVC, htmx
islands, command/query boundaries and PostgreSQL-backed source truth. It is
functional evidence only: user rejected visual fidelity to the subsequently
approved reference package. The current implementation is not an accepted
visual authority; follow `docs/ui-visual-fidelity-migration-plan.md` and its
coverage matrix for the structural rebuild and visual acceptance.

| Area | Visual routes | Migrated surfaces |
| --- | ---: | --- |
| Reception | 1 | Dashboard, search/results, direct client creation, profile, membership, visit, payment, freeze, card and correction partials |
| Owner | 3 | Membership Types, Non-Working Days, Staff Accounts, including both NonWorkingDay preview/correction workspaces |
| Reports | 5 | Daily, Ending Soon, Low Remaining, Negative Clients, Inactive Clients |
| Audit | 2 | Audit Timeline and Client History |
| Public/status | 4 | Login, Logout, AccessDenied and Error |
| Total | 15 visual page files plus `/` alias | 16 visual route entries, 14 workflow partials and 5 shared composition partials |

`SetLanguage` remains a POST transport endpoint and has no visual page.

## Current shared implementation (functional baseline only)

- `_Layout.cshtml` selects an authenticated `app-frame` or branded public
  frame without weakening page authorization.
- `_AppNavigation.cshtml` owns grouped Main/Owner navigation, exact active
  route semantics and Owner-only rendering. Server authorization remains the
  enforcement boundary.
- `_CurrentSession.cshtml` shows account, role, session and device context.
- `_Icon.cshtml` reads the local `wwwroot/images/bodylife-icons.svg` sprite.
  `lucide-LICENSE.txt` records the upstream icon license.
- `wwwroot/images/bodylife-logo.png` is the approved supplied BodyLife mark
  with its dark background removed and an alpha channel retained.
- `site.css` owns the shared light palette, focus ring, 44px touch targets,
  semantic surfaces, responsive shell and area-specific presentation.

No external fonts, icons, image CDN or client-side visual framework was added.

## UX contract changes

### Direct Create Client

`Create Client` no longer depends on performing a search first.

1. The existing `IQueryPermissionResolver` resolves
   `clients.create` against the canonical Admin-or-Owner policy.
2. Allowed actors always receive the action panel with a fresh idempotency key.
3. It is collapsed initially and can be opened directly.
4. A successful zero-result search opens it; only Card mode copies the query
   into the card field.
5. Failed validation or duplicate acknowledgement keeps submitted values and
   the open panel.
6. Success uses the existing command and canonical profile reread, retains
   search context, and returns the panel collapsed beside the selected profile.

No command, authorization, duplicate-warning, audit, transaction or
idempotency behavior moved into Razor or JavaScript.

### Semantic action states

- Blue: primary navigation and ordinary submit.
- Green: active/success/confirmed source state.
- Amber: ending, low remaining, preview or acknowledgement attention.
- Red: negative, canceled, destructive and correction risk.
- Violet: Owner-only, audit or restricted context.

Color is always paired with visible text, status chips, borders or local icons.
Cancel Visit remains a reasoned, acknowledged command and keeps its stable
htmx/data selectors.

## Preserved automation and interaction contracts

The migration retains:

- all form methods, handlers, antiforgery fields, input names and route values;
- all `hx-*`, `data-busy-*`, stable target IDs, indicators and sync behavior;
- NonWorkingDay confirmation tokens, scope fingerprints and idempotency keys;
- report/audit row IDs, status/origin attributes, pagination and drill-downs;
- canonical rereads after every successful mutation;
- server-owned Membership formulas and report totals.

## Responsive acceptance

- Primary tablet: `1024x768` with sidebar-aware Reception and Client History
  collapse at `1100px`.
- Phone: `390x844`, top two-column navigation followed by one operational
  content column.
- Interactive targets are at least `44x44px`; focus-visible uses the blue focus
  token; no action depends on hover.
- Long names, IDs, audit payloads and report rows wrap without horizontal page
  scrolling.

## Functional automated evidence (not visual-fidelity acceptance)

- Release solution build: 0 warnings, 0 errors.
- `dotnet format --verify-no-changes`: passed.
- Domain tests: 401/401 passed.
- Web tests: 292/292 passed.
- PostgreSQL 16.14 Infrastructure tests: 536/536 passed.
- Focused style/direct-create/report/history UI tests: 16/16 passed.
- Full Playwright UI suite: 128/128 passed with no skips.
- 143 full-page PNG captures were produced across Reception, Owner, Reports,
  Audit and History states for tablet/phone visual inspection.
- EF migration listing succeeded through
  `20260720173659_AddBusinessAuditRecordedTimelineIndex`; no schema change is
  part of this migration.
- A clean Release `./scripts/validate.sh` completed against PostgreSQL 16.14 in
  3m54.739s with all four suites green: 1,357/1,357 tests and no skips.

## Functional review result (not visual-fidelity acceptance)

Independent read-only reviews covered shared shell/navigation, Reception,
Owner, Reports/Audit and the integrated diff. Findings for root-route active
navigation, 1024px breakpoints, direct-create collapse after canonical reread,
language hover contrast and Reports section semantics were fixed and
re-reviewed cleanly.

The reviews and evidence above establish functional behavior, preserved
contracts and baseline accessibility checks. They do not establish side-by-side
approval, composition fidelity or acceptance against the approved external
reference package. Milestone 10 remains complete. This correction does not
start Milestone 11 or change an accepted ADR.
