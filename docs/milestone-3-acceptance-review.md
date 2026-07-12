# Milestone 3 acceptance review

Review date: 2026-07-12.

Source of truth: `docs/implementation-roadmap.md` Milestone 3, ADR-008, `docs/interaction-contracts.md`, `docs/data-architecture.md`, `docs/ui-workflows.md`, and implementation progress through Step 43.

## Decision

Milestone 3 is accepted as complete.

The Clients/Search foundation satisfies the milestone tasks, acceptance criteria, and required test areas. Work may move to Milestone 4 MembershipTypes. This is not a production-readiness claim, and it does not pull Memberships calculations, client history, reports, or audit timeline UI forward from their owning later milestones.

## Completed foundation

| Roadmap item | Status | Evidence |
|---|---|---|
| Clients and card-assignment schema | Done | Migrations `20260710111409_AddClientsSearchStorage` and `20260710113814_AddDuplicateWarningAcknowledgements` create canonical client, historical card, and duplicate-acknowledgement storage with foreign keys, checks, and search/timeline indexes. |
| Identity normalization | Done | `ClientSearchNormalizer` owns card, phone, last-four, and ordered full-name normalization with culture and invalid-input unit coverage. |
| Client commands | Done | `CreateClient`, `UpdateClient`, and `AssignOrChangeCard` handlers enforce canonical actor/session authorization, validation, idempotency, transaction boundaries, audit, stable errors, and reread targets. |
| Current-card uniqueness | Done | Partial unique indexes enforce one current client per normalized card and one current card per client; command handlers map expected unique conflicts to stable errors. |
| Duplicate warning workflow | Done | The duplicate query returns exact normalized phone/name warning pairs; create/update commands require the exact current acknowledgement set and persist actor/reason evidence. |
| Reception search | Done | `SearchClients` supports exact/partial card, normalized name/phone, last four, inactive inclusion, deterministic pagination, server warnings, and exact-card-only auto-open. |
| Canonical profile shell | Done | `GetClientProfile` returns current identity/card/status/version, Clients warnings, empty Memberships composition placeholder, and server-provided implemented actions. |
| Business audit | Done | Client create/update and card assign/change/clear append audit entries with actor account type, role, session/device, correlation/idempotency context, origin, and before/after summaries in the command transaction. |
| Reception UI | Done | Razor Pages plus htmx provide search, result selection, profile, create, update, and card workflows with canonical rereads, inline errors, busy/disabled submission, and progressive full-page fallbacks. |

## Acceptance criteria

| Acceptance criterion | Status | Notes |
|---|---|---|
| Client can exist without card number. | Done | Storage and `CreateClient` integration tests cover cardless clients; phone Playwright creation proves the canonical profile and no-card warning. |
| Exact unique current card match auto-opens; partial/non-unique matches do not. | Done | PostgreSQL search tests prove exact-card priority and null auto-open for partial/name/phone/last-four results; tablet and phone Playwright cover exact and ambiguous paths. |
| Duplicate current card assignment is blocked by DB constraint and command validation. | Done | Both partial unique indexes have direct migration/storage tests; existing-card and concurrent cross-client command cases commit only one complete workflow. |
| Phone/name duplicate warnings block create/update until explicitly acknowledged. | Done | Duplicate query and command tests cover exact warning sets, stale/extraneous/missing acknowledgements, and rollback; create/update UI tests prove both warning rows require checkbox plus reason. |
| Card change is separate from client update and requires reason for replace/clear. | Done | `UpdateClient` leaves card history unchanged. `AssignOrChangeCard` owns assign/change/clear, expected-current stale checks, reason validation, history, and action-specific audit. |
| Profile rereads canonical state and shows server-provided allowed actions. | Done | Profile query tests reread after identity/card commands and verify action permissions; all mutation PageModel handlers rebuild the canonical workspace after success. |
| Client and card audit entries include accountable context and before/after summaries. | Done | PostgreSQL command suites assert action type, actor/account/session/device, correlation/idempotency, origin, related refs, reasons/comments, and before/after content with rollback on rejection. |
| Search works by card, name, normalized phone, and last four digits. | Done | Unit normalization tests plus PostgreSQL search tests cover every mode, ranking, inactive visibility, pagination, validation, and no-match behavior. |

## Test coverage review

| Required test area | Status | Notes |
|---|---|---|
| Domain/application behavior | Done | Core normalization tests cover stable identity rules. PostgreSQL-backed command suites cover cardless creation, duplicate acknowledgements, permissions, validation, idempotency, stale state, atomicity, audit, and canonical reread targets without fake persistence. |
| PostgreSQL constraints and concurrency | Done | Migration/storage tests cover client/card checks, both partial unique indexes, history lifecycle, and raw concurrent assignments; command concurrency tests prove only one complete source/audit/idempotency workflow commits. |
| Search queries | Done | Exact/partial card, exact/partial name, exact/partial phone, last four, inactive clients, pagination, validation, permission denial, no writes, and no-match are covered. |
| Commands | Done | Dedicated CreateClient, UpdateClient, and AssignOrChangeCard suites cover Owner/named Admin/shared Reception, invalid canonical actors, errors, replay/change rejection, audit, rollback, and concurrency. |
| Reception Playwright | Done | Tablet and phone flows cover exact-card auto-open, ambiguous results, explicit profile open, no-match/create, update, card assignment/change/clear, stale refresh, duplicate warning review, busy state, and JavaScript-disabled search fallback. |
| Accessibility and touch | Done | Role/label locators prove accessible names. Tablet and phone smoke now measure search controls, mode segments, inactive control, profile actions, and result rows against the documented 44x44 px minimum and continue to assert no horizontal overflow. |

## Scope and risk check

- Search remains deterministic normalized exact/substring matching. No fuzzy search, merge workflow, scanner-specific identity, QR/NFC, or import cleanup surface was added.
- Card history remains append-preserving; application workflows do not hard-delete client or assignment history.
- The profile Memberships area is intentionally empty until Milestone 5 provides canonical membership state. Razor and search do not invent membership formulas.
- Recent client history, report drill-downs, and audit timeline UI remain owned by later roadmap milestones and are not Milestone 3 blockers.
- Client-facing accounts, public APIs, offline sync, online payments, and multi-tenant scope remain absent.

## Transition to Milestone 4

No Milestone 3 blocker remains. Milestone 4 may begin with the MembershipType persistence and public command/query contract, preserving Owner-only mutation, no hard delete, business audit, and the issue-time snapshot boundary required by Milestone 5.

## Validation baseline after Step 43

Focused touch acceptance:

```bash
DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Ui.SmokeTests/BodyLife.Crm.Ui.SmokeTests.csproj --configuration Release --nologo --filter 'FullyQualifiedName~ReceptionSearchAndProfileReadPathWorksOnTargetViewport'
```

Result: passed 2 tablet/phone tests.

Full repository gate:

```bash
CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh
```

Result: passed with Release build 0 warnings/errors, 34 core tests, 35 web tests, 107 PostgreSQL infrastructure tests, 15 Playwright smoke tests, and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
