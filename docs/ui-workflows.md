# BodyLife CRM UI workflows

Дата: 2026-07-07  
Статус: design draft for v1 implementation

Цей документ описує UI workflows для BodyLife CRM v1 як hybrid server-rendered web app. Він базується на `docs/interaction-contracts.md` і `docs/adr/003-ui-rendering-and-interaction-model.md`.

Це не design mockup і не специфікація pixel layout. Документ визначає, які screen/state, actions, warnings, confirmations, loading states, success states і failure states потрібні, щоб reception workflow можна було пройти без читання бізнес-вимог. Візуальну, layout and component consistency основу для реалізації задає `docs/ui-design-foundation.md`.

## 1. Scope and interaction principles

- Перший екран v1 - reception dashboard, а не generic CRUD.
- Сервер рендерить базові сторінки, форми, client profile, reports і admin/settings screens.
- Interactive islands допустимі тільки там, де вони прискорюють рецепцію: live search, compact results, membership status panel, warnings, quick actions і loading/duplicate-submit protection.
- Усі state-changing дії виконуються через server-side commands/actions.
- Після успішної дії UI перечитує canonical state із сервера через відповідний query. UI не рахує membership state локально і не залишає optimistic business values як правду.
- Reports читають canonical source records і Memberships public state/read models. Reports не дублюють membership formulas.
- Canonical instants показуються як `Europe/Kyiv` local time через active culture
  (`uk-UA` default, supported `en-US`) без `UTC`, zone name або offset suffix.
  HTML `datetime-local` означає Kyiv wall time; browser/device zone не змінює
  value. (ADR-017)
- V1 не включає full SPA/API як default, client self-service, online payments/POS, untracked direct edits, future import UI або explicit day-close workflow.

## 2. Tablet-first and phone-friendly expectations

- Tablet-first означає, що reception dashboard, search, profile summary, active membership panel, warnings і quick actions мають бути usable на планшеті як основний робочий пристрій рецепції.
- Phone-friendly означає, що ті самі workflows мають працювати у вузькому viewport без втрати дій: search, selected client, warnings, membership state і primary actions переходять у послідовний reading/action order.
- Touch interactions не повинні залежати від hover-only affordances.
- Localized date/time text must remain readable in tablet and phone layouts;
  no critical timestamp meaning may depend on a hidden timezone tooltip.
- State-changing buttons показують busy/disabled state після submit і не дозволяють повторний submit тієї самої дії.
- Confirmation and reason/comment UI для corrections/destructive actions має бути доступним і на tablet, і на phone.
- Compact layout не має приховувати critical warnings: negative balance, expired membership, zero visits, duplicate identity, changed-after-close і permission restrictions.

## 3. Workflow: reception dashboard

Today attention uses `GetReceptionAttentionSummary` for an explicit Kyiv business date, with the exact same lifecycle-active effective-end-date range semantics as the Ending Soon report. A successful empty result may display zero; permission, validation, recalculation and source-consistency failures display unavailable/error, never fake zero. Activity now uses `GetReceptionActivity` for the explicit Kyiv recorded business date: render empty/loading/failure states and label manual-backfill/paper-fallback rows with distinct occurred and recorded timestamps. The query retains an opaque cursor for stable continuation; the Wave 1 Home intentionally renders a five-row preview and routes to the complete History surface instead of paginating that compact card. The server returns UTC instants; Web alone converts instants to Kyiv using the active culture. DST filtering uses the half-open Kyiv day range, and compact Membership status/warnings are explicitly as of the selected recorded business date.

- User goal: швидко знайти клієнта, побачити його current membership state і виконати reception action без переходу в generic admin CRUD.
- Screen/state: server-rendered reception dashboard з поточним account/session/device indicator, search island, compact result area, selected client/profile area або empty state, daily report link/summary, allowed quick actions from server permissions.
- Primary actions: search by card/name/phone/last4; open exact card match; choose one result from multiple results; open client profile; mark visit; issue membership; add payment; add freeze; open daily report.
- Warnings: search ambiguity; duplicate identity warning when creating/updating client; inactive client operational status; membership negative/zero/expired/ending-soon/low-remaining warnings; stale state after another action; permission restriction for owner-only actions.
- Confirmations: none for ordinary search/open/profile navigation; required for warning acknowledgement before risky commands, destructive/correction actions, backdated/paper fallback entries, and owner-sensitive actions.
- Loading/duplicate-submit protection: search uses request cancellation or stale-result guards; quick actions use idempotency keys where required by command contracts; submitted buttons become disabled/busy until server response; UI rereads canonical state after success.
- Success state: dashboard shows the selected client with fresh profile summary, current membership state, warnings, recent history and available quick actions; daily counters may refresh from server reads.
- Failure state: validation or permission errors render near the action; duplicate submission is treated as a clear repeat outcome; stale/concurrency conflict asks the user to refresh the selected client state; recalculation failure blocks success and keeps previous canonical state visible.

## 4. Workflow: client search

- User goal: identify the right client quickly using card, phone, name or phone last four digits.
- Screen/state: reception dashboard search island backed by `SearchClients`, with compact result rows containing display name, phone display, current card, operational status, match type, current membership summary and warnings.
- Primary actions: type or scan search text; switch search mode only when needed; refine search text; open a result; clear search; start an authorized client creation path if no existing client is found.
- Warnings: partial or low-confidence matches; inactive clients; duplicate-looking clients; result warnings from Memberships query; no current card on client; non-unique partial card/name/phone matches.
- Confirmations: none for read-only search; duplicate acknowledgement is required only if the user proceeds to create/update a client and the command detects duplicate phone or similar name.
- Loading/duplicate-submit protection: show search loading for active request; ignore stale responses from older queries; do not auto-open from partial/non-unique matches; reads do not create business audit entries.
- Success state: exact unique card match may produce `auto_open_client_id`; all other matches render as compact selectable results.
- Failure state: query error keeps the search form available with retry; permission denial hides reception data; empty results clearly show that no existing client matched the submitted search.

## 5. Workflow: exact card match

- User goal: scan or enter a card and immediately open the only client who currently owns that card.
- Screen/state: reception search state after `SearchClients` with search mode `auto` or `card`.
- Primary actions: scan card; normalize and submit search; auto-open only when exact current card match is unique; otherwise show result list or no-match state.
- Warnings: no current card found; card-like input is partial or ambiguous; client has warnings after open; current card state changed since the user scanned.
- Confirmations: none for exact open; assigning or changing a card is a separate audited action and requires reason when replacing or clearing an existing card.
- Loading/duplicate-submit protection: card search shows immediate loading and ignores stale search responses; auto-open must only use canonical `auto_open_client_id` from the server.
- Success state: client profile opens with identity, current card, active membership panel, warnings and quick actions.
- Failure state: no match returns to search with alternatives for name/phone search; non-unique or partial match renders multiple results; stale state requires another search.

## 6. Workflow: multiple search results

- User goal: choose the correct client when search is not exact enough to auto-open.
- Screen/state: compact search result list from `SearchClients`; each row shows enough context to distinguish clients without opening generic edit screens.
- Primary actions: scan result rows; refine query; open one client; navigate back to results from profile if the wrong client was opened.
- Warnings: duplicate-looking names/phones; inactive operational status; membership warnings on each row; no current card; multiple clients sharing similar identity data.
- Confirmations: none to open a profile; duplicate acknowledgement is required only for create/update flows that intentionally proceed despite duplicate warnings.
- Loading/duplicate-submit protection: result selection disables only the selected open action while the profile request is loading; stale result rows should be replaced after search refresh.
- Success state: selected client profile opens and search context remains recoverable enough to return/refine.
- Failure state: selected client no longer exists or is inaccessible; UI shows `not_found` or `permission_denied` and keeps the search usable.

## 7. Workflow: client profile

- User goal: understand the client and safely perform the next reception action.
- Screen/state: server-rendered profile from `GetClientProfile`, including identity, current card, operational status, membership timeline, current membership state, warnings, recent visits/payments/freezes/non-working applications, audit/history summaries and allowed quick actions for the actor.
- Primary actions: mark visit; issue membership; add payment; add freeze; open/cancel relevant history facts; open daily report drill-down context; edit identity/card through explicit audited actions when needed.
- Warnings: duplicate identity acknowledgements where relevant; inactive client; zero/negative/expired membership; ending soon or low remaining; backfilled/paper fallback labels; changed-after-close markers; permissions hidden or disabled by actor role.
- Confirmations: ordinary profile viewing has none; correction/cancellation actions require confirmation and reason/comment; card replacement/clearing requires reason; risky visit/issue actions require warning acknowledgements when contracts require them.
- Loading/duplicate-submit protection: profile quick actions use server forms or interactive islands with busy/disabled submit state; idempotency keys are used for `IssueMembership`, `MarkVisit`, `CreatePayment`, `AddFreeze` and corrections/cancellations; after success the profile is reread.
- Success state: profile re-renders with fresh canonical membership state, warnings, history and allowed actions.
- Failure state: command errors render in the action context; `stale_state` or `concurrency_conflict` asks for profile refresh; `recalculation_failed` prevents UI from pretending the action succeeded.

## 8. Workflow: active membership panel

- User goal: see whether the client can visit today and what membership consequences an action will have.
- Screen/state: membership status panel backed by `GetMembershipState` or the current membership state inside `GetClientProfile`; shows snapshot, start/base/effective end dates, counted visits, remaining visits, negative balance, first negative visit date, extension days/explanation, last counted visit and warnings.
- Primary actions: use the one current active Membership for visit/freeze actions; choose one-off/trial when no membership should be consumed; mark visit; add freeze; issue new membership; open closure/debt history or extension explanation.
- Warnings: zero/negative remaining visits, expired by date, future-start ineligibility, Freeze conflict, ending soon, low remaining, active extension sources, predecessor closure effect, concrete coverable debt and unknown visible/non-coverable remainder. ADR-021 is accepted but this one-active presentation remains pending implementation.
- Confirmations: no confirmation for reading state; current-state acknowledgement remains required for zero/negative/expired conditions. Freeze coverage is blocking rather than acknowledgeable; one-off/trial must be an explicit non-membership choice.
- Loading/duplicate-submit protection: membership state reads can show loading independently of profile shell; state-changing actions disable submit and reread the panel after commit.
- Success state: panel reflects recalculated canonical state after visit/payment/membership/freeze/correction commands.
- Failure state: `membership_not_eligible`, `visit_during_freeze`, `not_found`, `stale_state`, `concurrency_conflict` or recalculation errors keep the previous state and ask for refresh/retry.

## 9. Workflow: warnings

- User goal: understand and intentionally handle conditions that change the risk or meaning of a reception action.
- Screen/state: warnings appear in search results, client profile, membership panel and command forms; warnings are server-provided, not locally invented.
- Primary actions: read warning; open related drill-down when available; acknowledge blocking warning; adjust the action; cancel the action; refresh state.
- Warnings: duplicate phone/name; zero remaining visits; negative balance; first negative visit date; expired membership; low remaining; ending soon; inactive membership type in issue flow; overlapping non-working/freeze extension explanation; backdated/paper fallback entry labels; changed-after-close marker; permission restriction.
- Confirmations: acknowledgement is required when command contracts require `duplicate_warning_not_acknowledged` or `warning_acknowledgement_required`; destructive/correction actions require confirmation plus reason/comment. ADR-020 membership issue coverage is read-only automatic preview, not a choice.
- Loading/duplicate-submit protection: acknowledgement is tied to the current command state and should become invalid when the underlying state changes; submit remains disabled while command is in flight.
- Success state: acknowledged command succeeds only after server revalidation; any warnings that still apply remain visible after the canonical reread.
- Failure state: missing acknowledgement returns a clear blocking error; changed warning state returns stale/conflict and asks the user to review the updated warnings.

## 10. Workflow: mark visit flow

- User goal: record that a client arrived and consume one counted membership visit when applicable.
- Screen/state: quick action on reception dashboard/profile, using client id, controlled visit kind, explicit selected membership id only for membership kind, business date/occurred_at, server-derived warning requirements and optional comment. Candidate rows show snapshot/name, start/effective-end dates, remaining visits and warnings.
- Time input/state: visible current/default time is Kyiv local wall time. A
  spring-forward gap returns localized validation without writes; a fall-back
  overlap uses the deterministic first occurrence defined by ADR-017.
- Primary actions: open mark visit against the displayed current active Membership; explicitly select eligible expired Membership or one-off/trial only where ADR-014 permits it; acknowledge every required zero/negative/expired condition; submit. Closed history never appears as a new-Visit choice.
- Warnings: selected membership does not belong to client; no eligible membership selected; future-start membership; zero/negative remaining visits; expired by date; Visit date covered by active Freeze; backdated or paper fallback entry requires reason/comment; possible stale membership state.
- Confirmations: typed current-state acknowledgement for zero/negative/expired states; deliberate one-off/trial choice; reason/comment for backdated/paper fallback entries. Active Freeze cannot be overridden: correct/cancel it or use one-off/trial without consuming the Membership.
- Loading/duplicate-submit protection: `MarkVisit` uses an idempotency key; submit is disabled/busy after click/tap; repeated scan/tap should not create multiple visits; command rereads profile/membership state after commit.
- Success state: visit appears in client history and daily visit count refreshes. Membership kind also recalculates the explicitly selected Membership; one-off/trial leaves every Membership unchanged.
- Failure state: `membership_not_eligible`, `visit_during_freeze`, `warning_acknowledgement_required`, `duplicate_submission`, `validation_failed`, `recalculation_failed` or `concurrency_conflict` are shown inline; selected context remains editable and no local membership values are applied.

## 11. Workflow: issue membership flow

- User goal: issue a concrete ordinary membership to a client with its mandatory exact cash sale Payment in the same workflow.
- Screen/state: profile quick action backed by `GetMembershipTypesForIssue` and `PreviewIssueMembership`; ordinary issue selector shows active membership types only.
- Primary actions: choose active ordinary membership type; choose start date; review snapshot preview, read-only exact price, base end date and expected initial state; submit `IssueMembership`. With JavaScript unavailable, native submit first renders the same signed preview, then a second submit issues it.
- Warnings: selected type became inactive; predecessor id/status and closure consequence; compact red current-negative balance; flat blue automatic oldest-first coverage result; clearly labeled red concrete remainder or amber unknown historical remainder; positive predecessor blocks issue even if expired/future-start; zero-capacity type is blocked when concrete negatives exist; forced backdated Membership can already be expired; preview is advisory and command revalidates its signed token in transaction.
- Confirmations: Reception never chooses a coverage method or quantity. A paper-fallback sale requires its batch row and explanation; manual opening-state backfill is a separate form and does not create a fake Payment. One-off negative closure remains a separate explicit workflow.
- Loading/duplicate-submit protection: issue form uses idempotency key; submit is disabled/busy; preview token/state does not replace command validation; after success UI rereads profile and membership state.
- Success state: profile opens with new membership state, copied issue-time snapshot, warnings, exact payment status and history/audit entries; if negative balance remains, the negative warning remains visible.
- Failure state: `membership_type_inactive`, `membership_not_eligible`, `stale_state`, `duplicate_submission`, `validation_failed`, `recalculation_failed` or conflict errors keep the form state and show the required correction.

## 12. Workflow: add payment flow

- User goal: record an accepted standalone v1 cash payment and make it visible in client history and daily cash report.
- Screen/state: payment quick action is separate from issue/coverage flows; form includes client, amount, currency, accepted standalone context, Kyiv-local `occurred_at` and comment. The server renders the default local value and stores the converted UTC instant.
- Primary actions: enter cash amount; choose a valid standalone context such as one-off/trial; submit `CreatePayment`; return to profile/report context.
- Warnings: amount must be greater than zero; method is cash in v1; `membership_sale` and `negative_closure` are unavailable because their complete workflows create their own exact Payments; paper fallback requires reason/comment and first-class batch row.
- Confirmations: no confirmation for normal current-day cash payment; correction/cancellation is handled by correction flows and requires reason/comment.
- Loading/duplicate-submit protection: `CreatePayment` uses idempotency key; submit is disabled/busy; repeated tap cannot create duplicate cash rows.
- Success state: payment appears in client history and selected day's daily cash report; Membership state is unchanged.
- Failure state: `validation_failed`, including a non-existent Kyiv DST time,
  `membership_not_eligible`, `duplicate_submission`, `not_found`,
  `permission_denied` or conflict errors render in the payment form and leave
  previous canonical state unchanged.

## 13. Workflow: add/cancel freeze flow

- User goal: add an individual freeze range that extends one issued membership, or cancel a mistaken freeze without deleting history.
- Screen/state: profile membership panel/history with a lifecycle-active Membership selected; add-freeze form includes start date, end date and reason/comment; cancel-freeze action is available from an existing freeze history row when permitted.
- Primary actions: add freeze range; review membership affected; submit `AddFreeze`; for cancellation, choose existing freeze, enter reason/comment, confirm and submit `CancelFreeze`.
- Warnings: inclusive date range must have `start_date <= end_date`; start must
  be no earlier than Membership start and no later than the current canonical
  effective end date; end may cross that effective end and is not clipped;
  active counted Membership Visits inside the range block the command until the
  Visit is canceled/corrected or the range changes; overlap with another Freeze
  or NonWorkingDay is allowed but extension days use the union calendar-day rule;
  backdated/paper fallback requires marker and reason/comment; after a
  closed/reconciled day may require Owner policy.
- Confirmations: add freeze requires reason/comment as part of the form; cancel freeze requires destructive confirmation plus reason/comment.
- Loading/duplicate-submit protection: `AddFreeze` and `CancelFreeze` use idempotency/duplicate-submit guards; submit buttons become disabled/busy; membership panel rereads after commit.
- Success state: history shows active or canceled freeze fact; membership effective end date, extension days and extension explanation refresh from canonical recalculation.
- Failure state: `membership_not_eligible`, `freeze_conflicts_with_visit`,
  `validation_failed`, `already_canceled`, `reason_required`,
  `day_closed_requires_owner`, `recalculation_failed` or concurrency errors are
  shown; no direct end-date mutation is displayed as success.

## 14. Workflow: daily report flow

- User goal: see a business day's visits, cash payments, corrections and drill-down explanations from canonical source records.
- Screen/state: server-rendered daily report backed by `GenerateDailyReport`, with business date, daily visit count, payment count, cash sum, visit/payment drill-down rows, cancellation/correction rows, changed-after-close labels when present and links to client history/audit.
- Primary actions: choose business date; load report; expand/open drill-down rows; navigate to client profile/history; start permitted correction from a row.
- Warnings: canceled visits/payments are excluded from totals; corrections after close change live totals but must be visible in drill-down/audit; report must not compute remaining visits, active status, negative balance or end dates itself.
- Date semantics: selected date is one Kyiv calendar day. The server queries the
  half-open UTC range between consecutive Kyiv midnights, including 23/25-hour
  DST days; the UI never labels that date as UTC.
- Confirmations: none for report reads; correction actions launched from report require confirmation and reason/comment.
- Loading/duplicate-submit protection: report load shows loading and replaces stale report responses by business date; read actions do not need idempotency keys.
- Success state: report displays canonical totals, source rows and explanation links for the selected date.
- Failure state: permission, invalid date or query failure leaves date selector available and shows retry; report does not show partial totals as authoritative if the query fails.

## 15. Workflow: correction flows

- User goal: fix mistaken visits, standalone payments, issued sales, negative
  closures, freezes or owner-only non-working periods while preserving
  explainable business history.
- Screen/state: correction entry point from profile history, daily report drill-down or owner non-working screen; form shows original source fact, affected client/membership/date/amount/range, required reason/comment and expected changed-after-close status when relevant.
- Timestamp presentation: original, replacement, occurred and recorded instants
  use one culture-aware Kyiv formatter with no timezone suffix. Replacement
  `datetime-local` follows the same DST validation as creation forms.
- Primary actions: cancel visit; correct or cancel standalone payment; replace or
  cancel issued sale; replace/cancel negative closure; cancel freeze; owner
  correct/cancel non-working period with previewed affected scope; submit the
  appropriate command.
- Warnings: correction after closed/reconciled day may be Owner-only except
  ADR-018 issued-sale replace/cancel, which remains Admin/Owner and receives a
  changed-after-close marker; original fact may already be canceled/replaced;
  generic payment correction cannot detach sale/closure Payment; correction can
  change membership state and report totals; changed-after-close marker must
  remain visible in reports/history; non-working correction can affect multiple
  Memberships and requires Owner preview/confirmation. The preview must make
  ADR-016 endpoint behavior explicit in business data: any inclusive overlap
  receives the full period, including when Membership starts or ends inside it.
- Confirmations: all destructive/correction actions require explicit confirmation plus reason/comment; non-working day add/range correction requires a token bound to the exact ordered Membership set and full applied ranges, and may fail if that scope changes.
- Loading/duplicate-submit protection: correction/cancellation commands use idempotency keys; submit stays disabled/busy; stale original fact or changed affected scope blocks commit and asks for refresh.
- Success state: original fact remains visible as canceled/corrected/replaced; replacement facts appear where applicable; membership recalculation and daily report totals refresh from canonical reads; audit entry is available for owner/admin history.
- Failure state: `already_canceled`, `reason_required`, `day_closed_requires_owner`, `preview_expired`, `affected_scope_changed`, `recalculation_failed`, `concurrency_conflict` or permission errors are shown without silently rewriting history.

## 16. Workflow: owner/admin differences

- User goal: make it clear which actions are available to reception/admin users and which require owner authority.
- Screen/state: all server-rendered screens and interactive islands receive actor context and allowed actions from server queries; UI shows current account/session/device so shared Reception/Admin accountability is honest.
- Primary actions: Admin/Reception can use reception dashboard, search, profile,
  mark visit, issue membership, add standalone payment, add freeze, daily
  report and current/open-day corrections where permitted. Admin/Reception and
  Owner may also replace/cancel an issued sale under ADR-018 even for an older
  day; Owner remains required for other closed-day actions when their policy
  says so. Owner can do all Admin actions plus owner-only catalog/settings,
  non-working day add/correction and owner-sensitive report/admin views.
- Warnings: disabled or hidden actions must match server permission checks; owner-only actions should explain permission requirement; shared account actions must still carry session/device/accountability in audit.
- Confirmations: owner-sensitive actions keep the same confirmation/reason requirements as commands; owner-only non-working day flow requires preview and confirmation of the exact affected scope, full applied period and estimated canonical change.
- Loading/duplicate-submit protection: permission is rechecked by the server on every command; client-side disabled state is convenience only; idempotency rules are identical across roles for duplicate-submit risk.
- Success state: successful commands create audit entries with actor/session/device context and rerender allowed actions from fresh server state.
- Failure state: `permission_denied` is shown as a business-safe blocked action; UI does not offer a local bypass and does not mutate visible business state.

## 17. Acceptance checklist for v1 reception slice

- ADR-021 tablet/phone acceptance (pending implementation): show `none`/one
  current Membership separately from closed history and aggregate debt; new
  Visit/Freeze choices never include closed Memberships. Zero/negative rollover
  preview and canonical reread show closure reason/successor and unchanged old
  sale Payment. Positive balance blocks Issue even when expired/future-start.
- Partial/full one-off and historical concrete coverage preserve warnings;
  unknown opening remainder is visible without a fake coverage action. Closed
  positive-crossing correction displays the exact lifecycle dependency and
  writes nothing. Stale predecessor, retry and duplicate submit refresh the
  same canonical state on tablet and phone.
- Reception can start from dashboard, search by card/name/phone, open profile, read membership state and perform mark visit without reading domain requirements.
- Exact unique current card match auto-opens; partial or non-unique matches never auto-open.
- Multiple result selection is compact and task-oriented, not generic CRUD.
- Active membership panel uses canonical Memberships state and keeps negative/zero/expired warnings visible.
- All state-changing quick actions have disabled/loading state and duplicate-submit protection.
- `IssueMembership`, `MarkVisit`, `CreatePayment`, `AddFreeze` and correction/cancellation commands use idempotency keys.
- Destructive/correction actions require confirmation and reason/comment.
- Daily report totals come from canonical source records and provide drill-downs to explain corrections/cancellations.
- Tablet viewport is the primary acceptance target, and phone viewport preserves every critical warning/action in a usable order.
- Owner/Admin differences are visible in available actions and enforced again by server commands.
- Representative winter/summer timestamps, DST gap/fold inputs and
  spring/fall 23/25-hour report dates pass tablet/phone acceptance without any
  visible UTC suffix or browser-zone dependency. (ADR-017)

## 18. ADR-018 sales and negative coverage workflow

- Issue form shows a selected active ordinary type and read-only exact snapshot price; it never offers omitted, under or over payment input.
- The conditional `Cover negative balance` Reception action starts with one filled red signed-balance plaque. Its exact one-off closure form is always visible; a neutral secondary route below opens the existing Issue Membership flow. This visual order does not preselect a method, type, quantity, negative-handling decision or start date. Concrete Visit provenance is a closed `+ / −` event disclosure at the end rather than summary fact tiles. Oldest-first consequences and expired preview remain server-owned; stale/inactive types fail before writes. Blocking errors and destructive corrections may also use red. An unknown opening/backfill remainder is explained in amber and exposes no synthetic coverage method.
- Mistaken one-off closure has a reason-required cancel/replace action that changes its items and Payment together; generic payment correction cannot detach it from the closure.
- Replacement/cancel form is Admin/Owner with required reason, explicit no-delta/refund notice, dependent-fact preview and blockers. Success rereads profile, history, audit and affected report state.
- Lifecycle closure/successor dependencies appear in correction preview. No
  action silently reactivates a predecessor or transfers unusable positive
  credit. Full one-off closes at canonical zero; partial keeps the sole active
  Membership, while already closed concrete debt remains separately coverable.
- Paper fallback entry creates/uses one numbered sheet batch and displays a stable line number on every row before reconciliation through daily report and audit/history.
