# Milestone 2 acceptance review

Review date: 2026-07-10.

Source of truth: `docs/implementation-roadmap.md` Milestone 2, ADR-012, `docs/architecture-baseline.md`, and the implementation progress through Step 27.

## Decision

Milestone 2 is accepted as complete.

The accountable access foundation now satisfies the Milestone 2 tasks, acceptance criteria and required test areas. Work may move to Milestone 3. This decision closes the auth/users/roles milestone; it is not a production-readiness claim, and every future state-changing workflow must still apply the established policy, actor/session, audit and canonical-reread contracts.

## Completed foundation

| Roadmap item | Status | Evidence |
|---|---|---|
| `accounts`, `sessions`, role/account-type persistence | Done | `bodylife.accounts`, `bodylife.sessions`, expiry/identity constraints, active-session index, migration backfill and PostgreSQL tests exist. |
| First Owner bootstrap without default credentials | Done | `bootstrap-owner` command and `docs/owner-bootstrap.md`. |
| Explicit Owner credential setup | Done | `set-owner-credentials` command and credential hashing. |
| Staff account lifecycle | Done | Owner-guarded create/update/activate/deactivate service is exposed through the Owner-only management surface for named Admin and shared Reception/Admin accounts. |
| Explicit staff credential setup/reset | Done | Owner-guarded credential setup/reset is exposed through the management surface with no default secrets, unique normalized login, hash-only storage and session revocation on reset. |
| Staff account business audit | Done | Lifecycle and credential mutations append accountable, secret-safe audit entries in the same PostgreSQL transaction and return the audit id. |
| Owner staff account management surface | Done | Owner-only Razor Page composes audited lifecycle/credential services, performs canonical rereads and exposes explicit Admin-denied state. |
| Login/logout/session tracking | Done | `/Login`, `/Logout`, database-backed 12-hour sliding expiry, active-session cookie validation and authenticated UI smoke tests. |
| Inactive account rejection | Done | `AccountLoginServiceRejectsInactiveAccountWithoutSession`. |
| Server-side authorization policies | Done | Owner-only, Admin+Owner, current/open-day correction and after-day-close policies with web tests. |
| Actor/session/correlation context for commands | Done | `IBodyLifeRequestContextResolver` and command envelope tests. |
| Current account/session/device UI indicator | Done | Shared layout partial and authenticated tablet/phone smoke assertions. |
| Query permission result shape | Done | `QueryPermissionSet`, resolver and policy outcome tests. |
| Auth/permission technical logs with masking | Done | Structured login/logout/session-rejection logs, authorization forbidden handler and masking/omission tests. |
| Denied permission audit policy | Done | Documented as technical-log-only unless a future Owner policy requires business audit. |

## Acceptance criteria

| Acceptance criterion | Status | Notes |
|---|---|---|
| Owner can authenticate | Done | Owner bootstrap plus explicit credentials and UI smoke login are in place. |
| Owner can manage/activate named Admin/shared Reception/Admin accounts | Done | `/Owner/StaffAccounts` supports create, rename, credential setup/reset and activate/deactivate with server policy, command envelope, audit and canonical reread. |
| Shared Reception/Admin actions identify shared account and session/device | Done | Credential setup and authentication integration tests prove the shared account type, role, session and device context are preserved honestly. Future business audit must continue using that identity. |
| Owner-only commands are rejected server-side for Admin/shared accounts | Done | Policy tests prove Admin/shared claims are denied by Owner-only and after-close policies; the current Owner management surface also proves an explicit Admin denied state. Future commands must continue calling these policies. |
| Admin+Owner reception commands receive valid actor/session context | Done | The common command envelope carries validated actor/session/correlation context for future business commands. |
| UI displays current account/session in reception/admin surfaces | Done | Tablet and phone smoke tests cover the current reception shell and honest shared-session identity contract. |
| Permission-denied results do not mutate business state and are visible to the user | Done for Milestone 2 | Query permission results expose stable denied states; Owner-guarded services reject before mutation; Playwright proves named Admin sees the explicit Owner-only denied page. Each future command UI retains the same obligation in its owning milestone. |
| Technical logs avoid passwords, tokens and unnecessary personal data | Done | Auth log tests check omission of raw credentials, login names, display names, device labels and token-like query values. |

## Test coverage review

| Required test area | Status | Notes |
|---|---|---|
| Authentication integration tests for Owner | Done | PostgreSQL-backed login tests and Playwright login smoke. |
| Authentication integration tests for named Admin/shared Reception/Admin | Done | PostgreSQL-backed staff credential tests authenticate both account types through `AccountLoginService`. |
| Authentication tests for inactive accounts | Done | Login rejects inactive accounts without creating a session. |
| Authorization tests | Done | Owner-only, Admin+Owner and day-close placeholder policies covered. |
| Session persistence/expiry tests | Done | PostgreSQL tests cover migration backfill, expiry constraints/index, login persistence, renewal, exact-boundary expiry, ended/claim-mismatched rejection and canonical active counts; Playwright proves database expiry returns to login. |
| Command envelope tests | Done | Actor/session/correlation id are available to future commands. |
| UI smoke current session display | Done | Tablet and phone covered. |
| Owner account-management UI and Admin denial | Done | Playwright covers the full Owner flow on tablet/phone and proves named Admin receives the explicit Owner-only denied state. |
| Logging masking tests/review checks | Done | Auth technical log tests cover secret and personal-data omission. |
| Account-management audit integration tests | Done | PostgreSQL tests cover required context, audit ids, secret omission, denied/no-op behavior, constraints and source/audit rollback. |

## Transition to Milestone 3

No Milestone 2 blocker remains. Milestone 3 may start with the Clients/Search domain normalization contract and focused tests before persistence and UI work.

Cross-cutting obligations remain active: future commands must enforce server policies, use the common actor/session envelope, append required business audit, avoid UI-only authorization and reread canonical state after mutation. These are implementation guardrails for later milestones, not unfinished Milestone 2 scope.

## Validation baseline after Step 27

Full gate run after database-backed active-session expiry closed the final test gap:

```bash
DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh
```

Result: passed with Release build 0 warnings/errors, 11 core tests, 35 web tests, 44 PostgreSQL infrastructure tests, 6 authenticated Playwright smoke tests and EF migration listing through `20260710093311_AddSessionExpiry`.
