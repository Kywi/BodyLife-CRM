# UI localization inventory

Status: complete for the post-Milestone 10 bilingual UI hardening step.

Supported UI cultures are `uk-UA` (default) and `en-US` (alternative). This
inventory covers user-visible server-rendered pages, htmx fragments, Web-layer
validation and error presentation, accessibility text, formatting and the
representative tablet/phone visual matrix. It does not start Milestone 11.

## Localization contract

- Localization is owned by `BodyLife.Crm.Web`. Domain and Infrastructure do
  not depend on `IStringLocalizer` and persisted business facts are unchanged.
- Resources are split by semantic Web zone: `Shared`, `Authentication`,
  `Reception`, `Owner`, `Reports`, `Audit` and `Validation`. Resource keys are
  semantic identifiers rather than English source text.
- The culture provider order is the ASP.NET Core culture cookie, then
  `Accept-Language`, then the `uk-UA` default. Only `uk-UA` and `en-US` are
  supported; malformed and unsupported values fall back safely.
- The language selector is present in the shared layout, including Login. It
  uses an antiforgery-protected POST, accepts only the two supported cultures,
  writes the culture cookie, permits only local return URLs and preserves the
  current authenticated route and htmx culture.
- Visible dates, timestamps, decimal numbers, money and counts use the active
  culture. HTML date inputs, hidden values, route/query values, identifiers,
  tokens and persisted payloads remain invariant.
- User-facing command/query failures are selected from stable status/error
  codes and fields in the Web layer. Raw exception text and arbitrary command
  messages are not rendered. Unknown presentation values use localized,
  fail-closed fallbacks.
- Ukrainian count grammar is implemented only in the Web presentation layer
  and covers the one/few/many categories, including the acceptance values 1,
  2, 5 and 21.

## Coverage legend

- **Complete** means the `en-US` and `uk-UA` resource sets cover the zone.
- **Mapped** means validation/status/error output is localized in Web rather
  than exposing arbitrary Application, Domain or exception text.
- **T/P** means tablet and phone workflow viewport checks exercise the zone.
- **Matrix** means the bilingual screenshot matrix covers the parent screen at
  Owner/tablet 1024x768 and/or named-Admin/phone 390x844. Owner-only routes are
  inaccessible to a named Admin by design; their phone layout is covered by
  Owner phone workflow tests.

## UI coverage matrix

| UI zone | User-visible text sources | English | Ukrainian | Validation/error coverage | Automated coverage | Tablet/phone visual verification |
| --- | --- | --- | --- | --- | --- | --- |
| Shared layout, navigation, session panel and language selector | `_Layout`, `_CurrentSession`, `_LanguageSelector`, `Shared` resources, selector PageModel | Complete | Complete | Unsupported culture, antiforgery and local-return failures are safe | localization contracts and selector/cookie/htmx/isolation Playwright | Matrix: Reception, Owner, Reports and Audit in both cultures; no overflow |
| Login, Logout, AccessDenied and Error | Razor Pages, Login PageModel, `Authentication` and `Shared` resources | Complete | Complete | Login ModelState, access denial and generic error copy localized | default/fallback culture, Login switching, auth and role smoke tests | Login touch controls checked; shared T/P layout rules apply |
| Reception dashboard shell | `Index`, `_ReceptionWorkspace`, shared layout, `Reception` resources | Complete | Complete | Query failure uses safe localized fallback | bilingual title assertions plus Reception workflow suite | Matrix T/P in both cultures |
| Client search and search results | `Index` PageModel, `_ReceptionWorkspace`, search presenter data | Complete | Complete | Empty, unavailable and invalid search states localized | exact card, partial results, clear/search and htmx culture tests | Reception Matrix plus T/P search/profile captures |
| Client profile, status, warnings and history summaries | `_ClientProfile`, Reception view models/presentation helpers | Complete | Complete | Warning codes and unknown values map to localized safe fallbacks | profile/read-path, warning, history and navigation smoke tests | Reception Matrix plus T/P canonical-profile captures |
| Create client | `_CreateClientForm`, `Index` PageModel, `ReceptionCommandErrorLocalizer` | Complete | Complete | ModelState, occupied card and duplicate acknowledgement mapped | duplicate review, idempotency and canonical reread smoke test | Tablet workflow capture and viewport fit |
| Update client | `_UpdateClientForm`, `Index` PageModel, duplicate presenter | Complete | Complete | Validation, duplicate acknowledgement and stale state mapped | tablet/phone duplicate, stale refresh and canonical reread tests | T/P workflow captures and viewport fit |
| Card assignment | `_CardAssignmentForm`, `Index` PageModel | Complete | Complete | occupied card, validation and stale state mapped | change/clear, duplicate, stale and idempotency smoke tests | Tablet workflow captures and viewport fit |
| Issue membership | `_IssueMembershipForm`, issue view model, warning/command mappings | Complete | Complete | input, inactive type, negative decision, stale/warning and payment failures mapped | bilingual representative action plus tablet/phone issue workflow | T/P form/success captures; Reception Matrix |
| Mark and cancel visit | `_MarkVisitForm`, `_CancelVisitForm`, visit view models and warning mappings | Complete | Complete | confirmation, warning acknowledgement, freeze conflict and stale state mapped | membership/one-off/trial, negative, freeze, cancellation and duplicate-submit tests | T/P form/success/cancellation captures; Reception Matrix |
| Create and correct/cancel payment | `_AddPaymentForm`, `_CorrectPaymentForm`, payment view models and formatter | Complete | Complete | amount, occurred time, context, reason, confirmation and command failures mapped | `uk-UA` comma/dot binder coverage, canonical browser submission in both cultures, tablet/phone create/correct/cancel and report launch tests | T/P form/success/correction captures; Reception Matrix |
| Add and cancel freeze | `_AddFreezeForm`, `_CancelFreezeForm`, freeze view models and formatter | Complete | Complete | date range, active visit conflict, confirmation, stale and permission failures mapped | tablet/phone add/cancel, recalculation, audit and duplicate-submit tests | T/P form/history/success captures; Reception Matrix |
| Membership Types | Owner Razor Page/PageModel, `Owner` resources, plural and culture formatters | Complete | Complete | create/edit/deactivate validation, stale, concurrency and lifecycle failures mapped | catalog/create/edit/deactivate T/P smoke tests and localized owner assertion | Owner/tablet Matrix both cultures; Owner phone viewport tests |
| Staff Accounts | Owner Razor Page/PageModel, `Owner` resources | Complete | Complete | permission, validation, protected Owner, duplicate login and generic failures mapped | lifecycle/credentials/session accountability plus technical-log correlation tests | Owner layout rules and tablet workflow viewport fit |
| Non-Working Days preview, confirmation and correction | Owner page, two htmx partials, preview/correction view models, `Owner` resources | Complete | Complete | form fields and stable query/command statuses mapped; untrusted raw messages rejected | PostgreSQL-backed preview/confirm/correct/cancel tests and audit explanation tests | Owner T/P viewport and touch checks; localized Owner screen assertion |
| Daily report | Razor Page/PageModel, `ReportsPresentation`, `Reports` and `Shared` resources | Complete | Complete | invalid filters, permission and unavailable states mapped | canonical totals/drill-down/correction navigation plus bilingual report assertion | Reports Matrix T/P both cultures |
| Ending Soon report | Razor Page/PageModel, warning mapper, plural/date helpers | Complete | Complete | invalid filters, permission and unavailable states mapped | filtering, paging, warning and extension navigation T/P tests | Reports parent Matrix plus T/P viewport fit |
| Low Remaining report | Razor Page/PageModel, warning mapper, plural/date helpers | Complete | Complete | invalid filters, permission and unavailable states mapped | filtering, paging, warnings and profile navigation T/P tests | Reports parent Matrix plus T/P viewport fit |
| Negative Clients report | Razor Page/PageModel, warning mapper, plural/date helpers | Complete | Complete | invalid filters, permission and unavailable states mapped | canonical negative state, opening state, paging and navigation T/P tests | Reports parent Matrix plus T/P viewport fit |
| Inactive Clients report | Razor Page/PageModel, status/plural/date helpers | Complete | Complete | invalid filters, permission and unavailable states mapped | threshold, no-visit, current/last membership and paging T/P tests | Reports parent Matrix plus T/P viewport fit |
| Audit Timeline | Timeline Razor Page/PageModel, `AuditPresentation`, typed action explanation factories | Complete | Complete | filter/status failures localized; malformed or inconsistent payloads fail closed | all 26 action kinds, filters, paging, Owner/Admin and malformed-payload tests | Audit Matrix T/P in both cultures |
| Client History | History Razor Page/PageModel, `ClientHistoryRowPresenter`, `AuditPresentation` | Complete | Complete | invalid filter, not-found and source-inconsistent states mapped | source facts, corrections/cancellations, paging, Owner/Admin and report links | Audit parent Matrix plus T/P viewport fit |

## Automated localization checks

The focused Web and UI suites cover:

- exact supported/default cultures, provider order and middleware placement;
- cookie persistence, `uk-UA -> en-US -> uk-UA`, local return URLs,
  unsupported cultures, antiforgery and `<html lang>`;
- htmx fragment culture and browser-context isolation;
- exact resource-key parity for every semantic resource family and detection
  of missing-resource fallback in production UI;
- localized ModelState and safe command/query error mapping;
- warning-code mapping and unknown-code fallbacks;
- Ukrainian plurals for 1, 2, 5 and 21;
- `uk-UA` comma/dot and `en-US` dot decimal binding, plus successful canonical
  browser payment submission in both cultures;
- culture-aware display dates/times/money while machine values stay invariant;
- all 26 audit action explanations and Client History rows in both resource
  sets.

## Visual evidence

The bilingual screenshot matrix contains 14 full-page captures under the
temporary validation directory:

- Owner/tablet 1024x768: Reception, Membership Types, Daily report and Audit
  Timeline in `uk-UA` and `en-US`;
- named-Admin/phone 390x844: Reception, Daily report and Audit Timeline in
  `uk-UA` and `en-US`.

Every matrix page passed the automated horizontal-overflow check. The
representative Ukrainian and English captures were inspected for wrapping,
language selector state, touch controls, warnings and critical actions. The
existing workflow tests additionally exercise phone/tablet layouts for issue,
visit, payment, freeze, reports, Membership Types and Non-Working Days.

## Intentional non-localized values

The following remain unchanged by design:

- the BodyLife CRM brand;
- user-entered names, Membership Type names, reasons and comments;
- canonical currency codes such as `UAH`;
- identifiers, card numbers, correlation IDs, fingerprints, idempotency keys,
  route values and hidden confirmation tokens;
- stored audit action codes and raw JSON payloads;
- persisted timestamps and all database source facts;
- CSS classes, JavaScript selectors and stable `data-*` test identifiers;
- technical logs and internal exception messages.

No business rule, Membership formula, command authorization/idempotency/audit
contract, persistence model, migration or accepted ADR was changed for this
hardening step.
