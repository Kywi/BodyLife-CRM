# BodyLife interactive UI prototype

Open https://kywi.github.io/BodyLife-CRM/ to review the current Ukrainian,
light-theme desktop prototype through staff access deactivation/reactivation (UI31).
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

The existing `ui-prototype-pages.yml` workflow publishes only this directory.
Other flat HTML pages and `assets/` remain earlier review references; they are
not part of the current entry page's navigation. Real application visual
acceptance is documented separately in `docs/ui-visual-fidelity-migration-plan.md`.

## Export record

- Export date: 2026-10-09.
- Source SHA-256: `3fb7dcea556530c771b5cb566d43e944cf603f4e014c66d6e2a063db1852c603`.
- Scope: UI31 staff access forms and history; UI30 name/login editing approved.
- Coordination: root integration plus independent `bodylife_reviewer`
  (loaded role target GPT-6.1 Sol/high; effective runtime telemetry unavailable).
- Local export checks: exact embedded fragment, sandbox/CSP, fixed date and
  whitespace validation passed. Browser checks at 1440 and 390 pixels passed
  demo login, search, staff rename, login/password change and new-credential
  sign-in, with no JavaScript errors or prototype horizontal overflow.

## UI31 — staff access

Owner → Staff access → Details now offers Disable access / Restore access.
Disabling requires a reason and ends the account’s simulated sessions while
retaining its login/password. Restoring requires no reason and does not restore
ended sessions. Both actions remain in history; an already-applied state is a
no-op. Pending accounts remain eligible but still need credentials to sign in.
This interaction is ready for design review; it is not yet user-approved.

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
