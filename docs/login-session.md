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
- Reception pages are not globally authorization-protected yet; server-side policies and account/session indicators are later Milestone 2 steps.

## Boundaries

- No client accounts or public portal are introduced.
- No default credentials are seeded.
- No account management UI is introduced.
- No business command is authorized from UI-only state.
