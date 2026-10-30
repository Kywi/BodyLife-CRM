# BodyLife interactive UI prototype

Open https://kywi.github.io/BodyLife-CRM/ to review the current Ukrainian,
light-theme desktop prototype through standalone payment change history (UI35).
The entry page replaces the earlier static review-lab dashboard.

## Trying the prototype

- Reception demo: login `reception`, password `demo`.
- The login page also has an Owner demo button for catalog, non-working periods
  and staff administration.
- Search, client profiles, visits, memberships, payments, history, reports and
  Owner interactions use fictional, deterministic records.
- The demo date is deliberately fixed at **2026-09-15**. Do not switch it to the
  viewer's current date: the review scenarios depend on this baseline.
- Changes live only in the current page's memory. Reloading resets the demo.
  Do not enter real client details or reuse a real password.
- Use HTTPS Pages or an HTTP localhost server. Staff password setup/reset uses
  Web Crypto, which requires a secure browser context; plain LAN HTTP is not a
  supported way to review every interaction.

This is a review prototype, not the ASP.NET Core application. Demo login and
Owner mode are simulated; server commands, authorization, persistence and
production acceptance remain the application's responsibility. Publication
neither approves the latest UI flow nor advances roadmap milestones.

## Export and deployment

`index.html` is a standalone export of the editable conversation fragment
`out/visualizations/bodylife-reception-01.html` (the local source is git-ignored).
It includes the renderer's sandboxed iframe (`sandbox="allow-scripts"`) and
both Content Security Policies. Do not remove the sandbox or relax those
policies. All prototype assets are embedded; no backend is needed.

To refresh it, use the visualize 1.0.46 `scripts/render.py` exporter:

```sh
python3 /path/to/visualize/scripts/render.py \
  out/visualizations/bodylife-reception-01.html \
  docs/ui-prototype/index.html --force --title 'Body Life — інтерактивний макет'
```

Then apply the same fixed-light wrapper styling as the local preview server
and set the outer/inner document language to `uk`. Keep the embedded fragment
unchanged. Validate the resulting iframe interactions before publication.

For the UI32, UI33 and UI34 refreshes the pinned exporter was unavailable. The exact escaped
source fragment was replaced once in the existing export; everything outside
that fragment, including both CSP policies and the sandbox, was preserved byte
for byte. The replacement was checked by reversing it against the prior export.

The existing `ui-prototype-pages.yml` workflow publishes only this directory.
Other flat HTML pages and `assets/` remain earlier review references; they are
not part of the current entry page's navigation. Real application visual
acceptance is documented separately in `docs/ui-visual-fidelity-migration-plan.md`.

## Export record

- Export date: 2026-10-30.
- Source SHA-256: `18d1467a9c82110b40de2035ff9817b9d52b792826e3a2a589238200d60fbc9b`.
- Scope: UI35 standalone payment history, ready for review; the user moved on
  from UI34 and its layout/navigation refinements on 2026-10-30.
- The audit surfaces use selected fictional fixture events and subsequent
  successful scoped changes from this page session, not a complete historic ledger.
- Local checks: exact fragment/mirror, unchanged sandbox/CSP wrapper, fixed
  date, two script blocks and whitespace checks. Chromium 1280/768/320 checks
  cover filtering, immutable snapshots, safe details, pagination, linked-account
  navigation, authorization, keyboard focus and calendar overlays.

## UI31 — staff access

Owner → Staff access → Details now offers Disable access / Restore access.
Disabling requires a reason and ends the account’s simulated sessions while
retaining its login/password. Restoring requires no reason and does not restore
ended sessions. Both actions remain in history; an already-applied state is a
no-op. Pending accounts remain eligible but still need credentials to sign in.
The user moved review forward to UI32 on 2026-10-28 after the layout refinements.

UI31 validation passed: reason limits and escaping; Owner/target guards;
error/stale rollback and retry; duplicate-submit and Escape protection;
credential preservation; same-state no-op; current-session recovery and
late-callback isolation; details, forms and history at 1024/320 pixels.
The source fragment, fixed date, mirror, sandbox/CSP and script syntax were
checked. Browser runs reported no JavaScript errors.

UI31 layout refinement: access actions occupy a separate full-width row inside
the account information panel. Login, status and session count use aligned
label/value columns in both active and inactive states.
Validated column alignment, separate action-row bounds, absence of overflow and
restore/disable clicks for active, inactive and pending accounts at 1280, 768
and 320 pixels in Chromium. Desktop and narrow screenshots were inspected.

UI31 credentials refinement: login and password actions are integrated into
field rows inside the same account panel. Short visible action labels retain
full accessible names. Pending accounts offer setup in the same row pattern;
small screens stack each action with its field.
Validated active/inactive/pending row bounds and 44px action targets at 1280,
768 and 320 pixels in Chromium; login change, password reset, Back and pending
setup entry passed with no page errors. Desktop/narrow screenshots inspected.

## UI32 — staff sessions (proposal)

Owner → Доступи → Олена Бондар → Деталі → Переглянути сеанси. Two fictional
active sessions allow checking single-session and all-session termination. Each
confirmation requires a reason and preserves credentials and account access.
Ended/expired rows remain visible without termination actions; empty accounts
have a clear empty state. New sessions appearing during confirmation require a
refreshed selection. The account count, reset, deactivation and login/logout
all use the same in-memory session rows.

Session metadata follows the existing SessionRecord model and fixed 12-hour
expiry policy. Per-session query and audited manual termination are UI proposals;
they are not implemented backend commands and do not change ADR acceptance.
The user approved UI32 on 2026-10-28; UI33 begins the staff-access audit surface.

Validation: local Chromium 1280/768/320 checks covered reason bounds/escaping,
single/all/empty/ended/expired states, busy/double-submit/Escape protection,
error retry, changed-scope reconfirmation, authorization and detached callbacks.
Credential reset/manual end/real demo login/logout, shared account and protected
Owner boundaries passed. Desktop and phone screenshots were inspected. Two
scripts parse; exact fragment/mirror, fixed date, sandbox/CSP and diff checks pass.
Independent explicit GPT-6.1 Sol/high source review found no actionable issues;
implementation used explicit GPT-6.1 Sol/medium and read-only discovery used
GPT-6 Luna/low. Runtime model telemetry is unavailable.

## UI33 — staff access change history (proposal)

Owner → Доступи → Історія змін. The two tabs keep staff administration and its
read-only history together. Filter by staff account, action group and recorded
Kyiv date; a chosen end date is inclusive. Newest entries appear first, with
stable ordering for changes at the same demo time and ten rows per page.

Details show the action, historical staff name, actor, device, before/after
where available, required reason and ended sessions. Event time appears
separately only when different from recorded time. Technical identifiers stay
collapsed. Open the current staff account and return to the same event or
filtered history. Missing accounts do not remove historical evidence.

Three selected demo entries cover account creation, configured reception login
and Maria's disabled access. Renaming, credential changes, access changes and
UI32 session termination append new snapshots. Reads never create entries.
Snapshots and copied session arrays are frozen; passwords and login values are
excluded. The existing demo date remains 2026-09-15 and reload resets all changes.

This first audit slice is limited to the Owner staff-administration surface.
It does not replace the general Owner/Admin GetAuditTimeline contract, add audit
of all prototype business actions, or implement a new backend command. UI32
manual session termination remains a proposed interaction. The user approved UI33 and its spacing refinement on 2026-10-28. UI34 adds
client contact/card history; broader financial/business audit remains later scope.

Validation: Chromium 1280/768/320 covered date and combined filters, inclusive
Kyiv midnight boundaries over recordedAt, empty/invalid ranges, details and
retained filters/pagination, source-account return, unchanged historical names,
real rename/session-end append, same-time ordering, frozen copied arrays,
escaped user content, excluded credentials, missing target and auth reset.
Keyboard focus is restored after filters, tabs, dates, load more and Back,
including an originating row pushed outside the loaded page. Calendar overlays
do not move the result list. Desktop/narrow screenshots inspected; no page
errors or horizontal overflow. Safari was not exercised.

Updated orchestrator: explicit GPT-6 Luna/low scout, GPT-6.1 Sol/medium worker
and fresh GPT-6.1 Sol/high independent reviewer dispatches accepted. Root fixed
the review's focus-restoration findings and the auth-reset browser finding.
Effective runtime model metadata is unavailable. No production milestone changed.

UI33 spacing refinement: the reset/count toolbar has 16px clearance below
the filter divider or date range, with the existing 12px gap before results.

## UI34 — client contact/card change history (proposal)

Sign in as Reception or Owner, search card **1042** (Марія Іваненко), then open
Контакти → Історія змін даних → Переглянути. Two selected example events show a
phone change by named administrator Olena and a card change by the shared
Reception account. Other untouched clients demonstrate the empty state.

The compact Contacts entry opens a separate read-only list. Filter by change
kind and recorded Kyiv dates, open details, compare Було / Стало and inspect the
reason or preserved duplicate acknowledgements. Same-number card reissue keeps
its explicit label, card number and reason. Back retains filters and loaded rows;
X/Escape returns focus to the Contacts entry. Shared accounts identify an account
and device, never a physical person. Technical identifiers remain collapsed.

Existing contact edits and card assignment/change/reissue now append frozen
snapshots with actor/session/device and stable same-time ordering. Old names,
values and duplicate explanations remain unchanged. Reads do not append events.
Submit-time actor context, open/current-form checks and auth/session checks
prevent late callbacks after dismissal, replacement or session changes.

This slice covers existing contact/card changes only. It does not add a client
creation audit surface, card-clearing workflow, full financial/business audit,
backend commands or persistence. Current card/search behavior and all membership
balances remain unchanged. The fixed demo date is still 2026-09-15.

Validation: Chromium 1280/768/320 checked filters, invalid/empty ranges, recorded
Kyiv midnight boundaries, list/detail/back and retained pagination, keyboard
focus, X/Escape return, calendar overlays and modal viewport bounds. Real shared
Reception edits, card change/reissue, named Admin actor snapshots, duplicate
acknowledgements, frozen nested values, escaping, error/stale retry, auth changes,
closed/replaced forms, cross-client detail denial and no-op reads passed.
Desktop/narrow screenshots inspected; no page errors or horizontal overflow.
Safari was not exercised. Two scripts parse; fragment/mirror, sandbox/CSP and
fixed date are preserved. User moved to UI35 on 2026-10-30 after refinements.

Coordination used the updated orchestrator: explicit Luna/low scout,
Sol6.1/medium worker and fresh Sol6.1/high reviewer. One transient worker capacity
failure was retried with unchanged model/effort; runtime telemetry unavailable.
Root applied the corrected prepared patch after two failed deliveries, then fixed
reviewed focus/lifecycle findings and browser-observed narrow modal placement.

UI34 layout refinement: each client audit row places Details in its own final
row, aligned right. Actor/device/time text uses the full preceding row and wraps
without moving the action to a different position.

UI34 detail refinement: each changed field is a single grouped Before/After
card using the existing borders, typography and soft green surface. Event context
and the reason are grouped separately. Narrow layouts stack each pair; audit
snapshots, acknowledgements, technical details and navigation remain unchanged.
Chromium 1280/768/320 checked paired values, multiple fields, long/empty text,
escaping, same-number reissue, Back/Escape and no horizontal overflow.

## Tablet viewport refinement (2026-10-29)

The standalone export now fills the visible browser viewport instead of growing
its iframe to the whole page content. Both CSP policies, iframe sandbox and
state bridge remain unchanged. The editable fragment applies app-height rules
only in standalone mode; ordinary inline rendering retains its existing sizing.
Global search remains visible; long page content scrolls in its main area.

Compact login, wider bounded dialogs and grouped Contacts use tablet width.
On touch Chromium at 1180x720, 820x1060 and 1024x768, normal login and Contacts
fit without scrolling; the two-fixture client audit list/detail and new-client
form also fit. Short screens, long histories, expanded/error forms retain
intentional scrolling with visible close/actions. ResizeObserver repositions
forms after dynamic duplicate warnings grow them.

Validation includes phone390/320, 360/420px short height, simulated outer viewport
pan, retained background/focus, editable final fields, busy/error states, owner
sections/credentials, calendar/help overlays and UI34 audit regression. No runtime
or ResizeObserver loop errors. iPad Safari keyboard/zoom still needs physical
device review; Chromium viewport emulation does not certify that behavior.
The production acceptance requirement is recorded in docs/ui-workflows.md 2.1.
This refinement does not approve UI34 or change the fixed 2026-09-15 demo date.

## Contextual dialog navigation refinement (2026-10-29)

Employee details now start with one contextual Back action: to the exact access
audit event or to Employees. Setup opened from details returns to that employee;
setup opened from the list or after creation returns to Employees. Opening a new
list target clears old audit context; audit filters remain when returning to the
event/history. The redundant bottom return/list pair is removed.

Confirmed navigation transitions reset the dialog scroll, including employee,
payment correction/backfill, freeze review and existing-membership entry flows.
Newly opened dialogs start at the top. Existing form errors, information/calendar
popovers, drafts, busy guards, domain behavior and fixed demo date are preserved.
Back controls stay in their forms and use the existing button design.

Navigation checks passed at 1180x720, 820x1060, 1024x768 and320x700, plus long
forms at320x420 and1024x420. Covered exact audit returns, nested setup origins,
new/list targets, creation, Escape/reopen, payment/freeze/opening drafts and
payment error recovery. UI33 audit/focus and UI32 credential regressions passed.
Screenshots at1180/320 were inspected; no browser errors. iPad Safari physical
keyboard/zoom/orientation still requires device review.


## UI35 — standalone payment change history (proposal)

Open **Журнал → Операція: Окремі оплати → PAY-DEMO-001 or PAY-DEMO-002 →
Історія змін оплати → Оплату виправлено: Деталі**.
The fictional Maria 1042 example records a correction from 150 to 200 UAH and
10:00 to10:05 on 14 September. It does not alter today's cash, visits or balances.

Each change groups its Before/After values, reason and captured account/device.
Short field comparisons sit side by side on wide screens; comments use full
width. Technical identifiers are collapsed. Payment details show concise status
and a History entry; reasons and Before/After live in history. The event has one
link to the current payment when different, with Back returning to the originating
journal/report/client history. Event Back returns to the list and source payment.
Journal filters remain. Long histories/details retain intentional scrolling.

Actual successful creation, correction and cancellation now append immutable
event snapshots. Multiple corrections keep their chain; cancellation preserves
the original amount and changes its status. Failed, stale, duplicate or detached
submissions add no successful event. Received time and recorded/action time are
distinct; backfilled creation retains its origin. Reconciled-day changes retain
the existing Owner restriction. Reads do not affect cash or membership balances.

This slice covers ordinary standalone payments only. Membership sales, debt
coverage and one-off/trial audit are separate remaining variants. It adds no
backend command or global audit menu. The fixed date remains 2026-09-15.
Browser evidence uses Chromium; physical iPad Safari review is still required.

Validation: Chromium touch viewports1180×720,820×1060,320×700 and a short
1024×420 screen covered list/detail/exact returns, snapshots, real creation and
two corrections followed by cancellation, backfilled receipt/action timestamps,
shared/named actor identity, no-op reads and membership preservation. Failed and
stale writes, duplicate submits, busy Escape, actor/session changes, missing links,
cross-scope denial, reconciled-day restrictions, same-value correction and long
escaped text passed. Final replacement/queued-close checks protect a newer busy
form's close control. The ordinary detail needs only28px overflow at1180×720;
long content remains scrollable. Source/export reversal retains the exact wrapper,
sandbox and CSP. Source is under1MB; no test globals are published.

## Current review queue (2026-10-30)

- UI1–34: reviewed, including the reported layout and navigation refinements.
  UI35 is the current proposal; do not repeat approved interactions without a
  specific defect or unmet requirement.
- Remaining business history: membership sale/correction, debt coverage,
  one-off/trial, visit/freeze and opening-balance changes; reuse the accepted
  history pattern and review only their distinct effects.
- Client creation audit; contact/card change history is already covered by UI34.
- Paper record entry/reconciliation: separate from approved backdated forms and
  tied to Milestone 11 readiness.
- Final end-to-end iPad Safari keyboard, zoom, orientation and error/recovery
  acceptance. Viewport emulation is supporting evidence only.


## UI35 refinement — simpler payment details (2026-10-30)

Removed Previous record and Replacement payment actions from payment details,
along with repeated correction reasons and the extra bottom Done action. There
is one top Back and one History entry. The corrected source still states that it
is excluded from daily cash; a canceled payment retains its cancellation reason.
Current replacements are labeled as current payments after correction.

History retains immutable comparisons and reasons. From an older source it
offers one forward action to the current same-client payment in the chain,
including a clearly labeled canceled terminal record. There is no link to the
old record and no self-link when already viewing the current payment. Missing,
foreign, linked or ambiguous targets leave historical information available.
Opening the current payment exits the nested event return path; its Back leads
to the original journal, report or client history. Pure history Back remains
event → list → source payment. No payment mutation or report rule changed.


Validation: Chromium touch1180×720,820×1060 and320×700 showed the corrected source
without scrolling. Journal/report/client-history origins, event/list/source
Back, self-link suppression, actual multiple corrections and cancellation, and
missing/foreign/linked/ambiguous current targets passed. Historical values and
cancellation reasons remain. Final history entry is a single full-width icon
button rather than a heading plus a cramped action. Physical Safari remains
unverified; no backend behavior changed.
