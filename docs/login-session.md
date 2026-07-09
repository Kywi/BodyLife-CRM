# Login and session tracking

This Milestone 2 step adds a minimal internal login/logout path backed by PostgreSQL account credentials and sessions. It is intentionally smaller than full account management.

## Owner credentials

Create the Owner identity first:

```bash
export BODYLIFE_BOOTSTRAP_OWNER_DISPLAY_NAME="BodyLife Owner"
./scripts/bootstrap-owner.sh
```

Then set explicit Owner credentials:

```bash
export BODYLIFE_OWNER_LOGIN_NAME="owner"
export BODYLIFE_OWNER_PASSWORD="use-a-long-secret-from-a-password-manager"
./scripts/set-owner-credentials.sh
```

The password must be at least 12 characters. The command stores a password hash only; it does not log the password or create a default secret.

## Login/logout

- `/Login` verifies the account credential, creates a row in `bodylife.sessions`, and issues an HTTP-only cookie.
- `/Logout` marks the session ended and signs out the cookie.
- Login failures use a generic user-facing error.
- Reception pages require an authenticated Owner, named Admin, or shared Reception/Admin session.

## Server-side policies

The web app registers these policy names for pages and future command handlers:

- `BodyLife.OwnerOnly`: Owner role plus Owner account type and active session claims.
- `BodyLife.AdminOrOwner`: Owner, named Admin, or shared Reception/Admin with active session claims.
- `BodyLife.CurrentOrOpenDayCorrection`: Owner/Admin/shared Reception/Admin for current/open-day correction resources.
- `BodyLife.AfterDayCloseCorrection`: Owner-only placeholder for after-close/reconciled-day corrections.

Shared Reception/Admin is allowed for daily Admin+Owner workflows and current/open-day corrections, but not Owner-only or after-close correction policies. Policy checks require session claims so future audit/command envelopes can distinguish account and session/device context.

## Request context resolver

Future Razor Page handlers, controllers, commands and queries should use `IBodyLifeRequestContextResolver` instead of reading authentication claims directly. The resolver turns the signed cookie claims and request correlation id into:

- `ActorContext` with account id, role, account type, session id and optional device label;
- `RequestCorrelationId` from `RequestCorrelationMiddleware`;
- `CommandEnvelope` for state-changing workflows.

The resolver rejects unauthenticated, malformed or role/account-type inconsistent claims. It does not replace command-specific authorization policies and does not query PostgreSQL for active-session expiry in this step.

## Current session indicator

Authenticated pages render the shared app shell with the current account display name, role/account type, device label and short session id. Shared Reception/Admin sessions must remain visibly labeled as shared session accountability; the UI must not imply a named physical person when the shared account is used.

## Query permission results

Query responses can include `QueryPermissionSet` / `QueryPermissionResult` so Razor pages and htmx fragments can show allowed, disabled or hidden actions consistently. Each result carries an action key, the policy name that should be enforced for the real command, and an optional denied reason code/message.

These query permission results are UI hints only. State-changing handlers must still call the server-side authorization policy for the command before mutation.

## Boundaries

- No client accounts or public portal are introduced.
- No default credentials are seeded.
- No account management UI is introduced.
- No business command is authorized from UI-only state.
