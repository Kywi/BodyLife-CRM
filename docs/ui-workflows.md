# BodyLife CRM UI workflows

Дата: 2026-07-07  
Статус: design draft for v1 implementation

Цей документ описує UI workflows для BodyLife CRM v1 як hybrid server-rendered web app. Він базується на `docs/interaction-contracts.md` і `docs/adr/003-ui-rendering-and-interaction-model.md`.

Це не design mockup і не специфікація pixel layout. Документ визначає, які screen/state, actions, warnings, confirmations, loading states, success states і failure states потрібні, щоб reception workflow можна було пройти без читання бізнес-вимог.

## 1. Scope and interaction principles

- Перший екран v1 - reception dashboard, а не generic CRUD.
- Сервер рендерить базові сторінки, форми, client profile, reports і admin/settings screens.
- Interactive islands допустимі тільки там, де вони прискорюють рецепцію: live search, compact results, membership status panel, warnings, quick actions і loading/duplicate-submit protection.
- Усі state-changing дії виконуються через server-side commands/actions.
- Після успішної дії UI перечитує canonical state із сервера через відповідний query. UI не рахує membership state локально і не залишає optimistic business values як правду.
- Reports читають canonical source records і Memberships public state/read models. Reports не дублюють membership formulas.
- V1 не включає full SPA/API як default, client self-service, online payments/POS, untracked direct edits, future import UI або explicit day-close workflow.

## 2. Tablet-first and phone-friendly expectations

- Tablet-first означає, що reception dashboard, search, profile summary, active membership panel, warnings і quick actions мають бути usable на планшеті як основний робочий пристрій рецепції.
- Phone-friendly означає, що ті самі workflows мають працювати у вузькому viewport без втрати дій: search, selected client, warnings, membership state і primary actions переходять у послідовний reading/action order.
- Touch interactions не повинні залежати від hover-only affordances.
- State-changing buttons показують busy/disabled state після submit і не дозволяють повторний submit тієї самої дії.
- Confirmation and reason/comment UI для corrections/destructive actions має бути доступним і на tablet, і на phone.
- Compact layout не має приховувати critical warnings: negative balance, expired membership, zero visits, duplicate identity, changed-after-close і permission restrictions.

## 3. Workflow: reception dashboard

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
- Primary actions: select the membership for visit marking if required; mark visit; add freeze; issue new membership; open extension explanation or history drill-down.
- Warnings: zero remaining visits; negative balance and first negative visit date; expired by date; ending soon; low remaining; active extension sources; missing/ambiguous current membership selection.
- Confirmations: no confirmation for reading state; warning acknowledgement is required before visit marking can proceed through zero/negative/expired states; if multiple active issued memberships are possible, UI must not silently choose between them without an explicit product rule.
- Loading/duplicate-submit protection: membership state reads can show loading independently of profile shell; state-changing actions disable submit and reread the panel after commit.
- Success state: panel reflects recalculated canonical state after visit/payment/membership/freeze/correction commands.
- Failure state: `membership_not_eligible`, `not_found`, `stale_state`, `concurrency_conflict` or recalculation errors keep the previous state and ask for refresh/retry.

## 9. Workflow: warnings

- User goal: understand and intentionally handle conditions that change the risk or meaning of a reception action.
- Screen/state: warnings appear in search results, client profile, membership panel and command forms; warnings are server-provided, not locally invented.
- Primary actions: read warning; open related drill-down when available; acknowledge blocking warning; adjust the action; cancel the action; refresh state.
- Warnings: duplicate phone/name; zero remaining visits; negative balance; first negative visit date; expired membership; low remaining; ending soon; inactive membership type in issue flow; overlapping non-working/freeze extension explanation; backdated/paper fallback entry labels; changed-after-close marker; permission restriction.
- Confirmations: acknowledgement is required when command contracts require `duplicate_warning_not_acknowledged`, `warning_acknowledgement_required` or explicit negative handling decision; destructive/correction actions require confirmation plus reason/comment.
- Loading/duplicate-submit protection: acknowledgement is tied to the current command state and should become invalid when the underlying state changes; submit remains disabled while command is in flight.
- Success state: acknowledged command succeeds only after server revalidation; any warnings that still apply remain visible after the canonical reread.
- Failure state: missing acknowledgement returns a clear blocking error; changed warning state returns stale/conflict and asks the user to review the updated warnings.

## 10. Workflow: mark visit flow

- User goal: record that a client arrived and consume one counted membership visit when applicable.
- Screen/state: quick action on reception dashboard/profile, using client id, visit kind, selected membership id or explicit non-membership context, business date/occurred_at and optional comment.
- Primary actions: open mark visit; confirm selected client and membership; choose visit kind if needed; submit; for zero/negative/expired states, explicitly acknowledge the warning before submit.
- Warnings: selected membership does not belong to client; no eligible membership selected; zero remaining visits; expired by date; negative visits are allowed only after explicit acknowledgement; backdated or paper fallback entry requires reason/comment; possible stale membership state.
- Confirmations: warning acknowledgement for zero/negative/expired states; reason/comment for backdated/paper fallback entries; no destructive confirmation for a normal current visit.
- Loading/duplicate-submit protection: `MarkVisit` uses an idempotency key; submit is disabled/busy after click/tap; repeated scan/tap should not create multiple visits; command rereads profile/membership state after commit.
- Success state: visit appears in client history, membership panel recalculates counted visits/remaining visits/negative state/first negative visit date, and daily visit count can refresh from server.
- Failure state: `membership_not_eligible`, `warning_acknowledgement_required`, `duplicate_submission`, `validation_failed`, `recalculation_failed` or `concurrency_conflict` are shown inline; no local membership values are applied.

## 11. Workflow: issue membership flow

- User goal: issue a concrete membership to a client, optionally recording the cash payment in the same workflow.
- Screen/state: profile quick action backed by `GetMembershipTypesForIssue` and `PreviewIssueMembership`; ordinary issue selector shows active membership types only.
- Primary actions: choose active membership type; choose start date; review snapshot preview, base end date and expected initial state; optionally enter cash payment amount/context; choose explicit negative balance handling when the client already has negative visits; submit `IssueMembership`.
- Warnings: selected type became inactive; client has negative balance; start date invalid; payment amount invalid; opening/backfill state requires source/reason; preview is advisory and command revalidates in transaction.
- Confirmations: explicit negative handling decision is required when negative balance exists; backfill/paper fallback issue requires reason/source; no hidden closure of negative visits by payment or new membership.
- Loading/duplicate-submit protection: issue form uses idempotency key; submit is disabled/busy; preview token/state does not replace command validation; after success UI rereads profile and membership state.
- Success state: profile opens with new membership state, copied issue-time snapshot, warnings, optional payment status and history/audit entries; if negative balance remains, the negative warning remains visible.
- Failure state: `membership_type_inactive`, `negative_decision_required`, `membership_not_eligible`, `duplicate_submission`, `validation_failed`, `recalculation_failed` or conflict errors keep the form state and show the required correction.

## 12. Workflow: add payment flow

- User goal: record a v1 cash payment and make it visible in client history and daily cash report.
- Screen/state: payment quick action on profile or issue-membership flow; form includes client, optional membership, amount, currency, payment context, occurred_at/business date and comment.
- Primary actions: enter cash amount; choose valid payment context; link membership only when relevant; submit `CreatePayment`; return to profile/report context.
- Warnings: amount must be greater than zero; method is cash in v1; linked membership must belong to the client; backdated/paper fallback entries require reason/comment; standalone payment does not automatically change membership formulas unless tied to issue/negative closure policy.
- Confirmations: no confirmation for normal current-day cash payment; correction/cancellation is handled by correction flows and requires reason/comment.
- Loading/duplicate-submit protection: `CreatePayment` uses idempotency key; submit is disabled/busy; repeated tap cannot create duplicate cash rows.
- Success state: payment appears in client history and selected day's daily cash report; membership panel refreshes only when the payment participates in issue/negative closure policy.
- Failure state: `validation_failed`, `membership_not_eligible`, `duplicate_submission`, `not_found`, `permission_denied` or conflict errors render in the payment form and leave previous canonical state unchanged.

## 13. Workflow: add/cancel freeze flow

- User goal: add an individual freeze range that extends one issued membership, or cancel a mistaken freeze without deleting history.
- Screen/state: profile membership panel/history with active membership selected; add-freeze form includes start date, end date and reason/comment; cancel-freeze action is available from an existing freeze history row when permitted.
- Primary actions: add freeze range; review membership affected; submit `AddFreeze`; for cancellation, choose existing freeze, enter reason/comment, confirm and submit `CancelFreeze`.
- Warnings: inclusive date range must have `start_date <= end_date`; freeze must belong to selected client/membership; overlap with NonWorkingDay is allowed but extension days are counted by union calendar-day rule; backdated/paper fallback requires marker and reason/comment; after closed/reconciled day may require Owner policy.
- Confirmations: add freeze requires reason/comment as part of the form; cancel freeze requires destructive confirmation plus reason/comment.
- Loading/duplicate-submit protection: `AddFreeze` and `CancelFreeze` use idempotency/duplicate-submit guards; submit buttons become disabled/busy; membership panel rereads after commit.
- Success state: history shows active or canceled freeze fact; membership effective end date, extension days and extension explanation refresh from canonical recalculation.
- Failure state: `validation_failed`, `already_canceled`, `reason_required`, `day_closed_requires_owner`, `recalculation_failed` or conflict errors are shown; no direct end-date mutation is displayed as success.

## 14. Workflow: daily report flow

- User goal: see a business day's visits, cash payments, corrections and drill-down explanations from canonical source records.
- Screen/state: server-rendered daily report backed by `GenerateDailyReport`, with business date, daily visit count, payment count, cash sum, visit/payment drill-down rows, cancellation/correction rows, changed-after-close labels when present and links to client history/audit.
- Primary actions: choose business date; load report; expand/open drill-down rows; navigate to client profile/history; start permitted correction from a row.
- Warnings: canceled visits/payments are excluded from totals; corrections after close change live totals but must be visible in drill-down/audit; report must not compute remaining visits, active status, negative balance or end dates itself.
- Confirmations: none for report reads; correction actions launched from report require confirmation and reason/comment.
- Loading/duplicate-submit protection: report load shows loading and replaces stale report responses by business date; read actions do not need idempotency keys.
- Success state: report displays canonical totals, source rows and explanation links for the selected date.
- Failure state: permission, invalid date or query failure leaves date selector available and shows retry; report does not show partial totals as authoritative if the query fails.

## 15. Workflow: correction flows

- User goal: fix mistaken visits, payments, freezes or owner-only non-working periods while preserving explainable business history.
- Screen/state: correction entry point from profile history, daily report drill-down or owner non-working screen; form shows original source fact, affected client/membership/date/amount/range, required reason/comment and expected changed-after-close status when relevant.
- Primary actions: cancel visit; correct or cancel payment; cancel freeze; owner correct/cancel non-working period with previewed affected scope; submit the appropriate command.
- Warnings: correction after closed/reconciled day may be Owner-only; original fact may already be canceled/replaced; correction can change membership state and report totals; changed-after-close marker must remain visible in reports/history; non-working correction can affect multiple memberships and requires owner preview/confirmation.
- Confirmations: all destructive/correction actions require explicit confirmation plus reason/comment; non-working day add/correction requires affected-scope preview/confirmation token and may fail if scope changes.
- Loading/duplicate-submit protection: correction/cancellation commands use idempotency keys; submit stays disabled/busy; stale original fact or changed affected scope blocks commit and asks for refresh.
- Success state: original fact remains visible as canceled/corrected/replaced; replacement facts appear where applicable; membership recalculation and daily report totals refresh from canonical reads; audit entry is available for owner/admin history.
- Failure state: `already_canceled`, `reason_required`, `day_closed_requires_owner`, `preview_expired`, `affected_scope_changed`, `recalculation_failed`, `concurrency_conflict` or permission errors are shown without silently rewriting history.

## 16. Workflow: owner/admin differences

- User goal: make it clear which actions are available to reception/admin users and which require owner authority.
- Screen/state: all server-rendered screens and interactive islands receive actor context and allowed actions from server queries; UI shows current account/session/device so shared Reception/Admin accountability is honest.
- Primary actions: Admin/Reception can use reception dashboard, search, profile, mark visit, issue membership, add payment, add freeze, daily report and current/open-day corrections where permitted. Owner can do all Admin actions plus owner-only catalog/settings, non-working day add/correction, owner-sensitive report/admin views and corrections after closed/reconciled day when policy requires owner authority.
- Warnings: disabled or hidden actions must match server permission checks; owner-only actions should explain permission requirement; shared account actions must still carry session/device/accountability in audit.
- Confirmations: owner-sensitive actions keep the same confirmation/reason requirements as commands; owner-only non-working day flow requires preview and confirmation of affected scope.
- Loading/duplicate-submit protection: permission is rechecked by the server on every command; client-side disabled state is convenience only; idempotency rules are identical across roles for duplicate-submit risk.
- Success state: successful commands create audit entries with actor/session/device context and rerender allowed actions from fresh server state.
- Failure state: `permission_denied` is shown as a business-safe blocked action; UI does not offer a local bypass and does not mutate visible business state.

## 17. Acceptance checklist for v1 reception slice

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
