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

## Staff account lifecycle foundation

`StaffAccountLifecycleService` is the backend foundation for Owner-managed named Admin and shared Reception/Admin accounts. It requires an Owner `CommandEnvelope`, creates only Admin-role staff account types, updates display names, activates/deactivates staff accounts, protects the Owner account from this workflow, and ends active sessions when a staff account is deactivated.

This lifecycle foundation does not create credentials, default passwords, sessions or account-management UI. Successful create, display-name update, activate and deactivate mutations append business audit entries in the same database commit.

## Staff credential setup and reset

`StaffCredentialsService` is the Owner-guarded backend path for setting or resetting credentials on named Admin and shared Reception/Admin accounts. It accepts the common `CommandEnvelope`, rejects non-Owner actors, protects the Owner account and returns stable validation, not-found and duplicate-login results.

- Login names are trimmed, normalized case-insensitively and remain unique through the PostgreSQL constraint.
- Passwords must be at least 12 characters and only the derived password hash is stored.
- No default staff credentials are generated or seeded.
- A credential reset atomically replaces the login/hash and ends active sessions for that staff account.
- Credentials may be prepared while an account is inactive, but login remains rejected until the Owner activates the account.

PostgreSQL integration tests authenticate both named Admin and shared Reception/Admin sessions through the normal `AccountLoginService` path. Successful setup and reset mutations append business audit entries, while denied, invalid and duplicate-login outcomes do not. This backend foundation does not yet expose an Owner-facing command/UI surface.

## Account management business audit

Staff account lifecycle and credential mutations append to `bodylife.business_audit_entries` through `BusinessAuditAppender`. The source mutation and audit row use one EF Core `SaveChanges` transaction, so either both persist or both roll back.

Canonical actions are `staff_account.created`, `staff_account.display_name_updated`, `staff_account.activated`, `staff_account.deactivated`, `staff_credentials.configured` and `staff_credentials.reset`. Entries include actor account/type/role, session/device, entity id, occurred/recorded timestamps, entry origin, reason/comment, correlation id and compact before/after JSON summaries. Credential audit never includes login names, raw passwords or password hashes.

Successful mutation results return the created `AuditEntryId`. Permission-denied, validation, not-found, duplicate-login and already-active/inactive no-op results do not create audit entries. General audit history queries/UI remain part of Milestone 10.

## Owner staff account management

Authenticated Owners can open `/Owner/StaffAccounts` from the shared application shell. The server-rendered Razor Page lists canonical staff account, credential and active-session state from PostgreSQL and exposes create, display-name update, credential setup/reset and activate/deactivate actions.

Every POST creates a fresh `CommandEnvelope` from the authenticated Owner account/session/correlation context and calls the audited staff services. The page uses Post/Redirect/Get so the rendered list always comes from a canonical reread rather than optimistic UI state. Credential reset and account deactivation require a reason; deactivation also requires confirmation and ends active sessions. State-changing forms disable their submit button while the request is running.

The entire `/Owner` folder uses the `BodyLife.OwnerOnly` policy, and the services repeat the Owner check before mutation. Named Admin and shared Reception/Admin sessions receive an explicit `Owner access required` page and cannot mutate account state.

## Query permission results

Query responses can include `QueryPermissionSet` / `QueryPermissionResult` so Razor pages and htmx fragments can show allowed, disabled or hidden actions consistently. Each result carries an action key, the policy name that should be enforced for the real command, and an optional denied reason code/message.

These query permission results are UI hints only. State-changing handlers must still call the server-side authorization policy for the command before mutation.

## Auth and permission technical logs

Login failures, successful logins, logout attempts and server-side permission denials produce structured technical log events. These logs are for debugging and support correlation only; they are not business audit history and denied permission attempts stay technical-log-only unless a future Owner policy explicitly requires audit.

The auth/permission log fields include `event_name`, `route_or_command`, `method`, `request_correlation_id`, `outcome`, stable result/error categories, account id, role/account type and session id when available. The logging policy omits raw passwords, tokens, login names, display names and device labels; personal fields are reduced to presence flags where useful.

Query permission results are advisory UI hints and do not create permission-denied technical log events. Real state-changing handlers and protected pages must still rely on server-side authorization policies, which are the enforcement point that logs forbidden attempts.

## Boundaries

- No client accounts or public portal are introduced.
- No default credentials are seeded.
- No generic account CRUD or Admin-accessible staff management is introduced.
- No general business-audit history UI is introduced yet.
- No business command is authorized from UI-only state.
