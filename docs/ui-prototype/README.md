# BodyLife UI review lab

This directory is a static, review-only GitHub Pages artifact for exploring
BodyLife CRM presentation at tablet and phone sizes. It contains deterministic,
fictional fixtures only: no backend, authentication, database, commands,
canonical reads, cookies, requests, persistence, real people, or secrets.

It is not the CRM application, a production hosting option, a Wave 1
visual-fidelity candidate, acceptance evidence, or an advancement of Wave 1
acceptance. Formal visual approval must still use the real authenticated Razor
Pages candidate with canonical data and the acceptance gates in
`docs/ui-visual-fidelity-migration-plan.md`.

## Routes and review states

| Route | Review states |
| --- | --- |
| `index.html` | `normal`, `empty`, `unavailable` |
| `clients.html` | `default`, `exact-card`, `search-multiple`, `no-result`, `query-failure`, `zero-visits`, `negative`, `expired`, `ending-soon`, `low-remaining`, `inactive`, `duplicate-warning`, `stale`, `freeze-blocked`, `backfill` |
| `reports-daily.html` | `default`, `changed-after-close`, `empty`, `unavailable` |
| `reports-ending-soon.html`, `reports-low-remaining.html`, `reports-negative-clients.html`, `reports-inactive-clients.html` | `default`, `empty`, `unavailable` |
| `audit-timeline.html` | `default`, `loading`, `empty`, `unavailable` |
| `client-history.html` | `default`, `empty`, `unavailable` |
| `owner-membership-types.html` | `default`, `validation`, `deactivation` |
| `owner-non-working-days.html` | `default`, `expired-token`, `scope-changed`, `correction` |
| `owner-staff-accounts.html` | default catalog / action previews |
| `login.html` | `default`, `invalid`, `disabled` |
| `access-denied.html` | authenticated Shared-role denial state |
| `logged-out.html`, `error.html` | focused public terminal states |

Use flat relative links. To view locally, open `index.html` in a browser or
serve this directory with a simple static server. Query-string states are
whitelisted; unknown `?state=` values fall back to each page's default.
Authenticated fixtures use the shared Reception/Admin context unless the page
is explicitly labelled `Owner · Олена К. (demo)`. Owner tools never appear in
the shared fixture navigation.

The Pages workflow publishes only this directory. Never place secrets,
production exports, real client data, session identifiers, or private
operational information here.

## Client profile action previews

The `default` and `exact-card` client states share one interactive,
non-persistent action surface. Its four keyboard-operable tabs preview marking
a visit, issuing a membership, recording a cash payment and adding a freeze.
Successful previews replace the submitted form with a confirmation state and
update the fictional profile/activity table until the page is reloaded.

These interactions are presentation fixtures, not application commands.
Terminal payments remain visibly unavailable because BodyLife v1 is cash-only.
Membership and freeze outcomes come from fixed server-preview fixtures; when a
date or range no longer matches a fixture, the preview is invalidated and
submission is disabled instead of calculating Membership state in JavaScript.
Warning, stale, blocked, search and error states do not simulate a successful
client action.
