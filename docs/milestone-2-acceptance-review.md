# Milestone 2 acceptance review

Review date: 2026-07-09.

Source of truth: `docs/implementation-roadmap.md` Milestone 2, ADR-012, `docs/architecture-baseline.md`, and the implementation progress through Step 26.

## Decision

Milestone 2 is partially complete but not accepted as complete.

The implemented foundation is strong enough to keep building inside Milestone 2, but the roadmap acceptance criterion for Owner-managed named Admin and shared Reception/Admin accounts is not yet satisfied. Do not move to Milestone 3 until the remaining account lifecycle and credential setup gaps are closed.

## Completed foundation

| Roadmap item | Status | Evidence |
|---|---|---|
| `accounts`, `sessions`, role/account-type persistence | Done | `bodylife.accounts`, `bodylife.sessions`, account constraints, session constraints and PostgreSQL tests exist. |
| First Owner bootstrap without default credentials | Done | `bootstrap-owner` command and `docs/owner-bootstrap.md`. |
| Explicit Owner credential setup | Done | `set-owner-credentials` command and credential hashing. |
| Staff account lifecycle | Done for backend foundation | Owner-guarded create/update/activate/deactivate service for named Admin and shared Reception/Admin accounts. |
| Explicit staff credential setup/reset | Done for backend foundation | Owner-guarded credential service, no default secrets, unique normalized login, hash-only storage and session revocation on reset. |
| Staff account business audit | Done for backend foundation | Lifecycle and credential mutations append accountable, secret-safe audit entries in the same PostgreSQL transaction and return the audit id. |
| Owner staff account management surface | Done | Owner-only Razor Page composes audited lifecycle/credential services, performs canonical rereads and exposes explicit Admin-denied state. |
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
| Owner can manage/activate named Admin/shared Reception/Admin accounts | Done after Step 26 | `/Owner/StaffAccounts` supports create, rename, credential setup/reset and activate/deactivate with server policy, command envelope, audit and canonical reread. |
| Shared Reception/Admin actions identify shared account and session/device | Done for auth foundation | Credential setup and authentication integration tests prove the shared account type, role, session and device context are preserved honestly. Future business audit must continue using that identity. |
| Owner-only commands are rejected server-side for Admin/shared accounts | Done for policy foundation | Policy tests prove Admin/shared claims are denied by Owner-only and after-close policies. Future commands still need to call these policies. |
| Admin+Owner reception commands receive valid actor/session context | Done for foundation | Command envelope can carry actor/session/correlation id. Business commands are not implemented yet. |
| UI displays current account/session in reception/admin surfaces | Done for current reception shell | Tablet and phone smoke tests cover the current reception page. |
| Permission-denied results do not mutate business state and are visible to the user | Partial | Query permission denial shape exists. Future command UIs still need visible denied states when module actions are added. |
| Technical logs avoid passwords, tokens and unnecessary personal data | Done | Auth log tests check omission of raw credentials, login names, display names, device labels and token-like query values. |

## Test coverage review

| Required test area | Status | Notes |
|---|---|---|
| Authentication integration tests for Owner | Done | PostgreSQL-backed login tests and Playwright login smoke. |
| Authentication integration tests for named Admin/shared Reception/Admin | Done | PostgreSQL-backed staff credential tests authenticate both account types through `AccountLoginService`. |
| Authentication tests for inactive accounts | Done | Login rejects inactive accounts without creating a session. |
| Authorization tests | Done | Owner-only, Admin+Owner and day-close placeholder policies covered. |
| Session persistence/expiry tests | Partial | Session creation/logout persistence exists. Session expiry/active-session database validation is not yet implemented. |
| Command envelope tests | Done | Actor/session/correlation id are available to future commands. |
| UI smoke current session display | Done | Tablet and phone covered. |
| Owner account-management UI and Admin denial | Done | Playwright covers the full Owner flow on tablet/phone and proves named Admin receives the explicit Owner-only denied state. |
| Logging masking tests/review checks | Done | Auth technical log tests cover secret and personal-data omission. |
| Account-management audit integration tests | Done | PostgreSQL tests cover required context, audit ids, secret omission, denied/no-op behavior, constraints and source/audit rollback. |

## Follow-up work before Milestone 3

1. Add active-session expiry validation and the required session expiry integration coverage.
2. Re-run Milestone 2 acceptance review after that gap closes.

## Validation baseline after Step 26

Full gate run after the Owner staff account management surface was added:

```bash
DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh
```

Result: passed with Release build 0 warnings/errors, 11 core tests, 34 web tests, 38 PostgreSQL infrastructure tests, 5 authenticated Playwright smoke tests and EF migration listing through `20260709204232_AddBusinessAuditEntries`.
