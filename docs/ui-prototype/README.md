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

## Current editable-lab baseline

`index.html` is the approved evolved composition baseline for this editable lab
and the remaining template migration: desktop rail, sticky header global search,
direct Create Client, honest account menu, Activity-first Home, and contextual
Attention/Today. It deliberately does **not** restore the old locked
sidebar/topbar/Quick Search composition. Locked assets are immutable historical
reference/audit input, while production Razor visual approval remains separate
and unproven.

## Routes and review states

| Route | Review states |
| --- | --- |
| `index.html` | `normal`, `empty`, `unavailable` |
| `clients.html` | `default`, `exact-card`, `search-multiple`, `no-result`, `query-failure`, `zero-visits`, `negative` (ADR-018 coverage fixture), `expired`, `ending-soon`, `low-remaining`, `inactive`, `duplicate-warning`, `stale`, `freeze-blocked`, `backfill`, `create-client`, `cancel-visit`, `cancel-visit-stale` |
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

The direct `create-client` anchor is available from the global header and
without a failed search. It contains surname, name, optional patronymic, phone,
optional card, comment, operational status and duplicate acknowledgement. The
`cancel-visit` anchor is linked from an eligible history row; it retains the
immutable original, requires reason and confirmation, identifies actor/day-close/origin,
and makes stale/conflict a blocked state. Both are deterministic preview-only
states: production commands, authorization, idempotency, audit and canonical
reread remain server responsibilities.

Duplicate review is a fixed server-response fixture, opened with
`clients.html?state=create-client&fixture=duplicate#client-create`. The browser
never infers a duplicate from a typed phone, name or card. The ordinary
`create-client` state keeps both the warning and acknowledgement hidden.

The `default` and `exact-card` client states share one interactive,
non-persistent action surface. Its four keyboard-operable tabs preview marking
a visit, issuing a membership, recording a cash payment and adding a freeze.
Successful previews replace the submitted form with a confirmation state and
update the fictional profile/activity table until the page is reloaded. An
ordinary issue preview always represents one ordinary Membership plus one
read-only exact cash Payment; it cannot omit, underpay or overpay the sale.

These interactions are presentation fixtures, not application commands.
Terminal payments remain visibly unavailable because BodyLife v1 is cash-only.
Membership and freeze outcomes come from fixed server-preview fixtures; when a
date or range no longer matches a fixture, the preview is invalidated and
submission is disabled instead of calculating Membership state in JavaScript.
Warning, stale, blocked, search and error states do not simulate a successful
client action.

Standalone payments are limited to one-off, trial and other accepted contexts;
they never bind a Membership and never substitute for a membership sale or
negative-coverage closure. ADR-018 review panels make replacement/cancellation,
oldest-first negative coverage and exact payments visible. Daily exposes paper
origin plus drill-down, while History and Audit expose first-class paper
provenance; no Milestone 11 paper-entry or reconciliation form is invented.
