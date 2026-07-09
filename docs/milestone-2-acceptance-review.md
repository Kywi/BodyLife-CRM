# Milestone 2 acceptance review

Review date: 2026-07-09.

Source of truth: `docs/implementation-roadmap.md` Milestone 2, ADR-012, `docs/architecture-baseline.md`, and the implementation progress through Step 21.

## Decision

Milestone 2 is partially complete but not accepted as complete.

The implemented foundation is strong enough to keep building inside Milestone 2, but the roadmap acceptance criterion for Owner-managed named Admin and shared Reception/Admin accounts is not yet satisfied. Do not move to Milestone 3 until the remaining account lifecycle and credential setup gaps are closed.

## Completed foundation

| Roadmap item | Status | Evidence |
|---|---|---|
| `accounts`, `sessions`, role/account-type persistence | Done | `bodylife.accounts`, `bodylife.sessions`, account constraints, session constraints and PostgreSQL tests exist. |
| First Owner bootstrap without default credentials | Done | `bootstrap-owner` command and `docs/owner-bootstrap.md`. |
| Explicit Owner credential setup | Done | `set-owner-credentials` command and credential hashing. |
| Login/logout/session tracking | Done | `/Login`, `/Logout`, `AccountLoginService`, session rows and authenticated UI smoke tests. |
| Inactive account rejection | Done | `AccountLoginServiceRejectsInactiveAccountWithoutSession`. |
| Server-side authorization policies | Done | Owner-only, Admin+Owner, current/open-day correction and after-day-close policies with web tests. |
| Actor/session/correlation context for commands | Done | `IBodyLifeRequestContextResolver` and command envelope tests. |
| Current account/session/device UI indicator | Done | Shared layout partial and authenticated tablet/phone smoke assertions. |
| Query permission result shape | Done | `QueryPermissionSet`, resolver and policy outcome tests. |
| Auth/permission technical logs with masking | Done | Structured auth log service, authorization forbidden handler and masking/omission tests. |
| Denied permission audit policy | Done for current scope | Documented as technical-log-only unless a future Owner policy requires business audit. |

## Acceptance criteria

| Acceptance criterion | Status | Notes |
|---|---|---|
| Owner can authenticate | Done | Owner bootstrap plus explicit credentials and UI smoke login are in place. |
| Owner can manage/activate named Admin/shared Reception/Admin accounts | Partial after Step 23 | Owner-guarded lifecycle service exists for create/update/activate/deactivate, but no credential setup/reset or Owner UI/command path exists yet. |
| Shared Reception/Admin actions identify shared account and session/device | Partial | Claims, context resolver, policy checks and UI indicator support it, and lifecycle service can create the account. There is still no supported path to authenticate a real shared account yet. |
| Owner-only commands are rejected server-side for Admin/shared accounts | Done for policy foundation | Policy tests prove Admin/shared claims are denied by Owner-only and after-close policies. Future commands still need to call these policies. |
| Admin+Owner reception commands receive valid actor/session context | Done for foundation | Command envelope can carry actor/session/correlation id. Business commands are not implemented yet. |
| UI displays current account/session in reception/admin surfaces | Done for current reception shell | Tablet and phone smoke tests cover the current reception page. |
| Permission-denied results do not mutate business state and are visible to the user | Partial | Query permission denial shape exists. Future command UIs still need visible denied states when module actions are added. |
| Technical logs avoid passwords, tokens and unnecessary personal data | Done | Auth log tests check omission of raw credentials, login names, display names, device labels and token-like query values. |

## Test coverage review

| Required test area | Status | Notes |
|---|---|---|
| Authentication integration tests for Owner | Done | PostgreSQL-backed login tests and Playwright login smoke. |
| Authentication integration tests for named Admin/shared Reception/Admin | Gap | Blocked by missing supported lifecycle/credential setup. |
| Authentication tests for inactive accounts | Done | Login rejects inactive accounts without creating a session. |
| Authorization tests | Done | Owner-only, Admin+Owner and day-close placeholder policies covered. |
| Session persistence/expiry tests | Partial | Session creation/logout persistence exists. Session expiry/active-session database validation is not yet implemented. |
| Command envelope tests | Done | Actor/session/correlation id are available to future commands. |
| UI smoke current session display | Done | Tablet and phone covered. |
| Logging masking tests/review checks | Done | Auth technical log tests cover secret and personal-data omission. |

## Follow-up work before Milestone 3

1. Add explicit credential setup/reset path for named Admin/shared accounts without default credentials.
2. Add minimal Owner-only account management surface or command path required by the roadmap acceptance criterion.
3. Add authentication integration coverage for named Admin/shared Reception/Admin accounts.
4. Re-run Milestone 2 acceptance review after those gaps close.

## Validation baseline for this review

Full gate run during this review:

```bash
DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh
```

Result: passed with Release build 0 warnings/errors, 11 core tests, 34 web tests, 17 PostgreSQL infrastructure tests, 2 authenticated Playwright smoke tests and EF migration listing through `20260709143654_AddAccountCredentials`.
