# BodyLife CRM implementation progress

Дата старту: 2026-07-08

## Milestone 1 plan

1. Solution/project scaffold.
2. Module folder structure.
3. EF Core/Npgsql/PostgreSQL setup.
4. Baseline migration and reviewable SQL workflow.
5. Health checks and structured logging foundation.
6. Analyzer/build gates and CI.
7. Test projects and PostgreSQL integration/migration harness.
8. Playwright smoke harness for tablet/phone reception entry.
9. Documentation/progress cleanup.

## Step 1 - Solution/project scaffold

Status: completed.

Scope:

- Create the solution file for the BodyLife CRM modular monolith.
- Add the hosted Razor Pages web project for the internal reception-first app.
- Add a core project placeholder for later business modules.
- Route `/` to the reception page so the first screen is not a generic CRUD or landing page.
- Keep EF Core, migrations, health checks, structured logging, analyzers, test projects and Playwright for later small steps.

Validation:

- `/tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --nologo` passed with 0 warnings and 0 errors.
- The SDK was installed locally for this session at `/tmp/bodylife-dotnet` because `dotnet` was not available in PATH.
- `graphify update .` completed for code changes.
- `graphify . --update` was attempted for the new markdown progress file but the CLI stopped because no semantic extraction API key/backend is configured.

Commit:

- `30009e9 build(infra): scaffold ASP.NET Core foundation`.

Next recommended step:

- Add the module folder structure and allowed shared primitives layout.

## Step 2 - Module folder structure

Status: completed.

Scope:

- Add core project folders for accepted top-level modules: Clients/Search, MembershipTypes, Memberships, Visits, Payments, Freezes, NonWorkingDays, Reports, Audit and Users/Roles.
- Add narrow shared primitives for IDs, Money, DateRange, actor/session context and request correlation id.
- Add command/query application conventions for common command envelopes, results and error taxonomy.
- Do not add persistence, EF Core, migrations, health checks, logging pipeline, test projects or business workflow implementations in this step.

Validation:

- `/tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --nologo` passed with 0 warnings and 0 errors.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal` passed.
- Module marker files exist for all accepted top-level module boundaries.
- `graphify update .` and `graphify update . --no-cluster` were attempted for code graph maintenance, but both stopped with `[Errno 95] Operation not supported`.
- `graphify . --update` was attempted for the progress markdown update, but the CLI stopped because no semantic extraction API key/backend is configured.

Commit:

- `3288dab build(shared): add module boundary conventions`.

Next recommended step:

- Add EF Core/Npgsql/PostgreSQL setup.

## Step 3 - EF Core/Npgsql/PostgreSQL setup

Status: completed.

Scope:

- Add a separate infrastructure project for EF Core/Npgsql persistence wiring.
- Add `BodyLifeDbContext` with PostgreSQL default schema and migrations history table settings.
- Register persistence in the hosted web app through configuration.
- Add a local development connection string placeholder for PostgreSQL.
- Keep baseline migrations, schema tables, PostgreSQL containers, health checks and integration tests for later small steps.

Validation:

- `/tmp/bodylife-dotnet/dotnet restore BodyLife.Crm.sln --nologo` passed.
- `/tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --nologo` passed with 0 warnings and 0 errors.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal` passed.
- `dotnet-ef dbcontext info` passed using temporary tool install at `/tmp/bodylife-dotnet-tools`; provider is Npgsql, database is `bodylife_crm_dev`, and migrations history table is `bodylife.__ef_migrations_history`.
- Web smoke returned `HTTP/1.1 200 OK` for `/`; local ASP.NET DataProtection logged warnings because this WSL profile has existing Windows-DPAPI-protected keys.
- `graphify update .` was attempted for code graph maintenance but stopped with `[Errno 95] Operation not supported`.
- `graphify . --update` was attempted for the progress markdown update but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(infra): wire EF Core PostgreSQL persistence`.

Next recommended step:

- Add the baseline migration and reviewable SQL workflow.

## Step 4 - Baseline migration and reviewable SQL workflow

Status: completed.

Scope:

- Add the initial EF Core baseline migration for the PostgreSQL `bodylife` schema.
- Keep the baseline free of business shortcut tables.
- Add a local `dotnet-ef` tool manifest.
- Add a script to generate idempotent migration SQL for review/deploy preparation.
- Keep real source-fact tables, constraints, indexes, seed/bootstrap data, integration tests and health checks for later small steps.

Validation:

- `/tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --nologo` passed with 0 warnings and 0 errors.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal` passed.
- `dotnet tool restore --tool-manifest .config/dotnet-tools.json` restored `dotnet-ef` 10.0.4.
- `dotnet-ef migrations list --no-connect` listed `20260708140900_InitialBaseline`.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/generate-migration-sql.sh /tmp/bodylife-baseline.sql` generated idempotent SQL that creates only the `bodylife` schema and EF migrations history table.
- PostgreSQL migration apply check could not run in this WSL distro because Docker, `psql` and `pg_isready` are not available. SQLite/EF InMemory was not used as a substitute.
- `graphify update .` completed for code graph maintenance.
- `graphify . --update` was attempted for the progress markdown update but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(infra): add baseline migration workflow`.

Next recommended step:

- Add health checks and structured logging foundation.

## Step 5 - Health checks and structured logging foundation

Status: completed.

Scope:

- Add JSON console logging with scopes enabled for structured technical logs.
- Add request correlation middleware that accepts `X-Request-Correlation-Id` or `X-Correlation-ID`, generates a safe fallback id and returns `X-Request-Correlation-Id` on responses.
- Add request outcome logging with `request_correlation_id`, environment, route/command, method, status code, duration, outcome and error class.
- Add health endpoints for local/staging monitoring: `/health/live`, `/health/ready` and `/health`.
- Add a PostgreSQL readiness check through EF Core `CanConnectAsync`.
- Keep CI, analyzer gate expansion, test projects, Testcontainers, Playwright and idempotency key storage for later small steps.

Validation:

- `/tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --nologo` passed with 0 warnings and 0 errors.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal` passed.
- Runtime smoke with `ASPNETCORE_ENVIRONMENT=Development` and `ASPNETCORE_URLS=http://127.0.0.1:5097` started the app successfully.
- `curl -i -H 'X-Request-Correlation-Id: step5-smoke-ordered' http://127.0.0.1:5097/health/live` returned `HTTP/1.1 200 OK`, JSON health output and response header `X-Request-Correlation-Id: step5-smoke-ordered`.
- JSON console logs for the smoke request included `request_correlation_id`, `environment`, `route_or_command`, `duration_ms`, `outcome` and `error_class`.
- `/health/ready` returned `503 Service Unavailable` in this WSL distro because PostgreSQL is not running/available; the check is wired but cannot be proven healthy until local/test PostgreSQL exists.
- Local HTTP smoke still logs the existing ASP.NET HTTPS-port warning when no HTTPS port is configured; the warning now carries the request correlation scope.

Commit:

- `build(infra): add health and structured request logging`.

Next recommended step:

- Add analyzer/build gates and CI.

## Step 6 - Analyzer/build gates and CI

Status: completed.

Scope:

- Add repository formatting defaults in `.editorconfig`.
- Pin the local SDK expectation in `global.json` to .NET SDK 10.0.301 with feature roll-forward.
- Strengthen `Directory.Build.props` so warnings, code analysis warnings and code style enforcement are part of build behavior.
- Add `scripts/validate.sh` as the shared local/CI validation gate.
- Add GitHub Actions CI for restore, Release build/analyzers, format verification and EF migration metadata listing.
- Keep test projects, PostgreSQL/Testcontainers migration apply checks and Playwright for later small steps.

Validation:

- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed.
- The validation gate restored local tools, restored the solution, built Release with 0 warnings and 0 errors, verified formatting and listed `20260708140900_InitialBaseline` with `dotnet-ef migrations list --no-connect`.
- The first validation run exposed UTF-8 BOMs in generated EF migration files after `.editorconfig` was added; the BOMs were removed and the gate passed.
- Real GitHub Actions execution was not run locally; `.github/workflows/ci.yml` was checked structurally through the same script it calls.
- `graphify update .` completed for code graph maintenance.
- `graphify . --update` was attempted for markdown progress updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(infra): add CI validation gate`.

Next recommended step:

- Add test projects and PostgreSQL integration/migration harness.

## Step 7 - Test projects and PostgreSQL integration/migration harness

Status: completed.

Scope:

- Add `tests/BodyLife.Crm.Tests` for fast unit/architecture gates.
- Add `tests/BodyLife.Crm.Infrastructure.Tests` for PostgreSQL-backed migration and readiness checks.
- Add unit tests for inclusive `DateRange`, accepted module names and command result/reread conventions.
- Add PostgreSQL integration tests that create an isolated disposable PostgreSQL database, apply EF migrations and verify the `bodylife` schema plus EF migrations history table.
- Add a PostgreSQL-backed `/health/ready` test through `WebApplicationFactory<Program>`.
- Extend `scripts/validate.sh` to run unit and infrastructure test projects.
- Extend GitHub Actions CI with a PostgreSQL service and `BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING` so PostgreSQL tests run in CI.
- Keep Playwright smoke harness for the next small step.

Validation:

- User reported the previous Step 6 GitHub CI workflow runs correctly before this Step 7 change.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed.
- Local validation restored packages/tools, built Release with 0 warnings and 0 errors, verified formatting, ran 5 unit tests successfully, and listed `20260708140900_InitialBaseline`.
- Local PostgreSQL integration tests were discovered and skipped because `BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING` is not configured in this WSL distro.
- GitHub Actions has been updated to provide a PostgreSQL service, so the same validation script should run the migration and `/health/ready` PostgreSQL tests there instead of skipping them.
- No SQLite or EF InMemory provider was introduced.
- `graphify update .` completed for code graph maintenance.
- `graphify . --update` was attempted for markdown progress updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `test(infra): add PostgreSQL migration harness`.

Next recommended step:

- Add Playwright smoke harness for tablet/phone reception entry.

## Step 8 - Playwright smoke harness for tablet/phone reception entry

Status: completed.

Scope:

- Add `tests/BodyLife.Crm.Ui.SmokeTests` as an xUnit + Microsoft.Playwright smoke project.
- Start the real Razor Pages web app through `dotnet run --no-build --no-launch-profile` on an isolated local Kestrel port during UI smoke tests.
- Verify the reception entry page renders on tablet and phone viewport sizes.
- Check the first screen contract stays reception-first: page title, heading, labeled search input, search button and client status region are visible.
- Check a simple GET search round-trip preserves the query and the layout does not require horizontal scrolling.
- Extend `scripts/validate.sh` so Playwright browser installation and UI smoke tests are part of the shared local/CI validation gate.
- Keep business workflows, htmx islands, real search results and command duplicate-submit tests for later milestones.

Validation:

- `PLAYWRIGHT_INSTALL_WITH_DEPS=1 DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed after installing local Playwright browser system dependencies.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed afterward using the standard validation command.
- The validation gate restored packages/tools, built Release with 0 warnings and 0 errors, verified formatting, ran 5 unit tests successfully, ran 2 Playwright smoke tests successfully and listed `20260708140900_InitialBaseline`.
- Local PostgreSQL integration tests were discovered and skipped because `BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING` is not configured in this WSL distro.
- The first attempt with Microsoft.Playwright 1.55.0 failed because this environment is `ubuntu26.04-x64`; the smoke project now uses Microsoft.Playwright 1.61.0, which supports the local runtime.
- The first browser launch attempt showed missing local Linux browser dependencies (`libasound.so.2`); running Playwright with `--with-deps` installed them. GitHub Actions will use `--with-deps` automatically because `CI=true`.

Commit:

- `test(ui): add Playwright smoke harness`.

Next recommended step:

- Do Milestone 1 documentation/progress cleanup and review remaining acceptance gaps before moving to Milestone 2.

## Step 9 - Local PostgreSQL validation configuration fallback

Status: completed.

Scope:

- Add a `scripts/validate.sh` fallback that can populate `BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING` from `src/BodyLife.Crm.Web/appsettings.Development.json`.
- Keep explicit environment variables authoritative so CI-provided PostgreSQL admin settings are not changed.
- Read only `ConnectionStrings:BodyLifeTestAdmin` for this fallback because the integration harness creates and drops disposable test databases and therefore needs a role with `CREATE DATABASE`.
- Do not treat the ordinary application `ConnectionStrings:BodyLife` value as an admin/test connection string.

Validation:

- `bash -n scripts/validate.sh` passed.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` was run.
- A temporary attempt to derive the admin connection from `ConnectionStrings:BodyLife` proved that the current local `bodylife` role can connect but does not have `CREATE DATABASE`; PostgreSQL integration tests correctly failed with `42501: permission denied to create database`.
- A diagnostic attempt with local `Username=postgres;Password=bodylife_dev_password` failed authentication, so no local superuser credential was assumed.
- Final validation falls back to the existing skip behavior until `ConnectionStrings:BodyLifeTestAdmin` or `BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING` points to a PostgreSQL role that can create disposable databases.

Commit:

- `build(infra): derive test postgres admin config`.

Next recommended step:

- Finish Milestone 1 documentation/progress cleanup and acceptance gap review before moving to Milestone 2.

## Step 10 - Milestone 1 acceptance review

Status: completed.

Scope:

- Add `docs/milestone-1-acceptance-review.md`.
- Compare implemented foundation work against Milestone 1 acceptance criteria.
- Record completed areas, partial local-environment gaps and remaining Milestone 1 follow-ups.
- Keep the step documentation-only; do not start Milestone 2 or broad business workflows.

Validation:

- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed.
- The validation gate restored tools/packages, built Release with 0 warnings and 0 errors, verified formatting, ran 5 unit tests successfully, ran 2 Playwright smoke tests successfully and listed `20260708140900_InitialBaseline`.
- Local PostgreSQL integration tests were discovered and skipped because no `BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING` or `ConnectionStrings:BodyLifeTestAdmin` is configured for a role with `CREATE DATABASE`.

Commit:

- `docs(infra): record milestone 1 acceptance review`.

Next recommended step:

- Add the Milestone 1 idempotency key storage foundation before starting Milestone 2.

## Step 11 - Local Docker PostgreSQL setup

Status: completed.

Scope:

- Add `docker-compose.yml` with a local PostgreSQL 17 service, health check and named data volume.
- Expose the development PostgreSQL container on host port `55432` to avoid collisions with existing local PostgreSQL instances on `5432`.
- Add `scripts/dev-postgres.sh` for local `up`, `wait`, `status`, `logs`, `down` and destructive `reset` workflows.
- Add `docs/local-development.md` with the local PostgreSQL workflow and the boundary that production should still use managed PostgreSQL.
- Add `ConnectionStrings:BodyLifeTestAdmin` to Development settings so `scripts/validate.sh` can run disposable PostgreSQL integration tests locally.
- Update the design-time EF fallback connection string to the Docker PostgreSQL port.
- Harden the migration history table assertion to check `pg_class`/`pg_namespace` instead of relying on `regclass` display formatting.

Validation:

- `docker compose -f docker-compose.yml config` passed.
- `bash -n scripts/dev-postgres.sh` passed.
- `./scripts/dev-postgres.sh up` started the local PostgreSQL container and readiness passed on `localhost:55432`.
- Host-side `psql` confirmed the `bodylife` role in the Docker PostgreSQL instance has `rolsuper` and `rolcreatedb`.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING="Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password" /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo` passed with 2 PostgreSQL tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed: 5 unit tests, 2 PostgreSQL infrastructure tests, 2 Playwright smoke tests and EF migration listing.

Commit:

- `build(infra): add local Docker PostgreSQL`.

Next recommended step:

- Add the Milestone 1 idempotency key storage foundation before starting Milestone 2.

## Step 12 - CI PostgreSQL readiness test fix

Status: completed.

Scope:

- Fix `PostgreSqlReadyHealthCheckTests` so the test web host replaces the already-registered `BodyLifeDbContext` with the disposable migrated PostgreSQL database.
- Avoid relying on `ConfigureAppConfiguration` for this test because the app reads the connection string during service registration.
- Add the health response body to the readiness assertion failure message for future diagnostics.

Validation:

- `ConnectionStrings__BodyLife='Host=localhost;Port=1;Database=wrong;Username=wrong;Password=wrong' BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo --filter FullyQualifiedName~PostgreSqlReadyHealthCheckTests` passed.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed with 5 unit tests, 2 PostgreSQL infrastructure tests, 2 Playwright smoke tests and EF migration listing.

Commit:

- `test(infra): fix ready health check database override`.

Next recommended step:

- Add the Milestone 1 idempotency key storage foundation before starting Milestone 2.

## Step 13 - Idempotency key storage foundation

Status: completed.

Scope:

- Add `bodylife.command_idempotency_keys` through EF Core/Npgsql migration without binding it to every business command yet.
- Store command name, idempotency key, request correlation id, actor/session context, entry origin, lifecycle status, timestamps and reread/audit result references needed for later duplicate-submit handling.
- Add PostgreSQL constraints for non-empty command/key/correlation/actor fields, accepted entry origins, accepted storage statuses and timestamp lifecycle consistency.
- Add a unique PostgreSQL index on `(command_name, idempotency_key)` and an expiry index for later cleanup.
- Add PostgreSQL-backed integration tests proving the migration creates the storage shape and rejects duplicate command keys and invalid status values.
- Update the Milestone 1 acceptance review so idempotency storage is no longer listed as a remaining gap.

Validation:

- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo` passed with 5 PostgreSQL infrastructure tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 5 unit tests, 5 PostgreSQL infrastructure tests, 2 Playwright smoke tests and EF migration listing for `20260708140900_InitialBaseline` and `20260709113419_AddCommandIdempotencyKeys`.
- `graphify update .` completed for code graph maintenance.
- `graphify . --update` was attempted for markdown progress/acceptance updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(infra): add idempotency key storage`.

Next recommended step:

- Start Milestone 2 with accountable Users/Roles and bootstrap ownership before adding reception business commands.

## Milestone 2 plan

1. Accounts/sessions persistence foundation.
2. Safe Owner bootstrap procedure without default production credentials.
3. Minimal login/logout/session tracking for the internal app.
4. Server-side authorization policies for Owner-only, Admin+Owner and correction/day-close placeholders.
5. Actor/session context resolver for commands and queries.
6. Current account/session/device UI indicator.
7. Permission result shape for queries while server policies remain authoritative.
8. Auth/permission technical logs with password/token masking.
9. Milestone 2 acceptance review and progress cleanup.

## Step 14 - Accounts and sessions persistence foundation

Status: completed.

Scope:

- Add `bodylife.accounts` and `bodylife.sessions` through EF Core/Npgsql migration.
- Represent Owner, named Admin and shared Reception/Admin account types with role constraints.
- Add a partial unique index so there can be only one Owner account in the one-gym v1 model.
- Add session/device metadata storage with account foreign key, active-session partial index and timestamp consistency checks.
- Keep login/logout, password/session secret storage, UI account indicator and default credentials out of this step.
- Add PostgreSQL-backed integration tests proving table/index/FK creation, single-owner enforcement, account-type/role consistency and session timestamp/account integrity.

Validation:

- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo` passed with 10 PostgreSQL infrastructure tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 5 unit tests, 10 PostgreSQL infrastructure tests, 2 Playwright smoke tests and EF migration listing for `20260708140900_InitialBaseline`, `20260709113419_AddCommandIdempotencyKeys` and `20260709120305_AddUsersRolesAccountsSessions`.
- `graphify update .` completed for code graph maintenance.
- `graphify . --update` was attempted for markdown progress updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(users): add accounts sessions storage`.

Next recommended step:

- Add the safe Owner bootstrap procedure without default production credentials.

## Step 15 - Safe Owner bootstrap procedure

Status: completed.

Scope:

- Add an application-hosted `bootstrap-owner` command path that runs before Kestrel starts.
- Add `OwnerBootstrapper` to create the first active Owner account identity through EF Core instead of raw SQL.
- Require explicit `BODYLIFE_BOOTSTRAP_OWNER_DISPLAY_NAME` or `BodyLife:Bootstrap:OwnerDisplayName`; no default Owner display name, password, token or session is created.
- Make the bootstrap idempotent: if the Owner account already exists, the command exits successfully without creating another Owner.
- Add `scripts/bootstrap-owner.sh` and `docs/owner-bootstrap.md` to document the safe procedure and boundaries.
- Keep login/logout, password/session secret storage, named Admin/shared account bootstrap and UI account indicator for later Milestone 2 steps.

Validation:

- `bash -n scripts/bootstrap-owner.sh` passed.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo` passed with 13 PostgreSQL infrastructure tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 5 unit tests, 13 PostgreSQL infrastructure tests, 2 Playwright smoke tests and EF migration listing.
- `graphify update .` completed for code graph maintenance.
- `graphify . --update` was attempted for markdown progress/bootstrap documentation updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(users): add safe owner bootstrap`.

Next recommended step:

- Add minimal login/logout/session tracking for the internal app.

## Step 16 - Minimal login/logout/session tracking

Status: completed.

Scope:

- Add PostgreSQL-backed `bodylife.account_credentials` with one credential row per account, unique normalized login name and password-hash constraints.
- Add password hashing and explicit Owner credential setup through `set-owner-credentials` / `scripts/set-owner-credentials.sh`.
- Add `AccountLoginService` that verifies credentials, rejects inactive accounts, creates session rows and marks sessions ended on logout.
- Add cookie authentication wiring plus minimal `/Login` and `/Logout` Razor Pages.
- Add `docs/login-session.md` and update the Owner bootstrap docs to keep credentials separate from identity bootstrap.
- Keep global page authorization, server-side role policies, account management UI and current account/session indicator for later Milestone 2 steps.

Validation:

- `bash -n scripts/bootstrap-owner.sh` and `bash -n scripts/set-owner-credentials.sh` passed.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo` passed with 17 PostgreSQL infrastructure tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 5 unit tests, 17 PostgreSQL infrastructure tests, 2 Playwright smoke tests and EF migration listing through `20260709143654_AddAccountCredentials`.
- `graphify update .` and `graphify update . --no-cluster` were attempted for code graph maintenance but stopped with `[Errno 95] Operation not supported`.
- `graphify . --update` was attempted for markdown progress/login documentation updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(users): add login session tracking`.

Next recommended step:

- Add server-side authorization policies for Owner-only, Admin+Owner and correction/day-close placeholders.

## Step 17 - Server-side authorization policies

Status: completed.

Scope:

- Add named ASP.NET Core authorization policies for Owner-only, Admin+Owner, current/open-day correction and after-day-close correction workflows.
- Require authenticated BodyLife session claims for policy success, so future commands cannot authorize from role claims alone.
- Keep shared Reception/Admin honest: allowed for Admin+Owner and current/open-day correction policies, rejected for Owner-only and after-close correction policies.
- Protect the reception Razor Page route with `AdminOrOwner`; health endpoints and login remain anonymously reachable.
- Add focused web policy tests and include the new web test project in `scripts/validate.sh`.
- Update Playwright smoke tests to authenticate against a temporary PostgreSQL database with real Owner credentials before checking the reception dashboard.
- Keep actor/session context resolver, permission result shape, current account UI indicator and auth/permission technical logs for later Milestone 2 steps.

Validation:

- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Web.Tests/BodyLife.Crm.Web.Tests.csproj --configuration Release --nologo` passed with 17 web authorization tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Ui.SmokeTests/BodyLife.Crm.Ui.SmokeTests.csproj --configuration Release --nologo` passed with 2 authenticated Playwright smoke tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 5 core unit tests, 17 web authorization tests, 17 PostgreSQL infrastructure tests, 2 authenticated Playwright smoke tests and EF migration listing through `20260709143654_AddAccountCredentials`.
- `graphify update .` was attempted for code graph maintenance but stopped with `[Errno 95] Operation not supported`; the partial `graphify-out/cache/stat-index.json` update was reverted and not committed.

Commit:

- `build(users): add authorization policies`.

Next recommended step:

- Add actor/session context resolver for commands and queries.

## Step 18 - Actor/session context resolver

Status: completed.

Scope:

- Add `IBodyLifeRequestContextResolver` for web entry points to resolve the current authenticated BodyLife actor/session and request correlation id.
- Add `BodyLifeRequestContext` and `CreateCommandEnvelope(...)` so future state-changing workflows can build the common command envelope from server-side context instead of UI-local state.
- Share the same strict claim parser between authorization policies and the request-context resolver.
- Reject unauthenticated, malformed or role/account-type inconsistent claims before command/query handlers receive actor context.
- Register the resolver through DI with `IHttpContextAccessor`.
- Keep active-session database validation, current account UI indicator, permission result query shapes and auth/permission technical logs for later Milestone 2 steps.

Validation:

- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Web.Tests/BodyLife.Crm.Web.Tests.csproj --configuration Release --nologo` passed with 25 web authorization/request-context tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 5 core unit tests, 25 web authorization/request-context tests, 17 PostgreSQL infrastructure tests, 2 authenticated Playwright smoke tests and EF migration listing through `20260709143654_AddAccountCredentials`.
- `graphify update .` completed for code graph maintenance.
- `graphify . --update` was attempted for markdown progress/login documentation updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(users): add request context resolver`.

Next recommended step:

- Add current account/session/device UI indicator.

## Step 19 - Current account/session/device UI indicator

Status: completed.

Scope:

- Add a shared `_CurrentSession` Razor partial in the app layout for authenticated pages.
- Display current account display name, account type/role, device label and short session id from `IBodyLifeRequestContextResolver`.
- Add a layout-level logout action next to the session indicator.
- Keep Login unauthenticated and indicator-free.
- Keep the indicator honest for future shared Reception/Admin sessions by displaying account type/session/device metadata rather than implying a physical person.
- Add responsive CSS for tablet/phone layouts without changing reception workflow behavior.
- Update authenticated Playwright smoke tests to assert the indicator is visible on tablet and phone.
- Add a web project reference to the UI smoke test project so focused smoke runs rebuild the app before launching with `--no-build`.
- Keep account management UI, permission result query shapes and auth/permission technical logs for later Milestone 2 steps.

Validation:

- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Ui.SmokeTests/BodyLife.Crm.Ui.SmokeTests.csproj --configuration Release --nologo` passed with 2 authenticated Playwright smoke tests after adding the web project reference.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 5 core unit tests, 25 web authorization/request-context tests, 17 PostgreSQL infrastructure tests, 2 authenticated Playwright smoke tests and EF migration listing through `20260709143654_AddAccountCredentials`.
- `graphify update .` completed for code graph maintenance.
- `graphify . --update` was attempted for markdown progress/login documentation updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(ui): add current session indicator`.

Next recommended step:

- Add permission result shape for queries while server policies remain authoritative.

## Step 20 - Query permission result shape

Status: completed.

Scope:

- Add `QueryPermissionResult`, `QueryPermissionSet` and denied reason codes in the application query layer.
- Represent query permissions with action key, required server policy, allowed/denied state and optional denied reason.
- Add `IQueryPermissionResolver` for web query/page composition to evaluate existing ASP.NET Core policies into advisory query permission results.
- Keep query permission results as UI hints only; command/page handlers must still enforce server policies before mutation.
- Add tests for permission shape trimming, lookup, duplicate action-key rejection, not-authenticated results and Owner/Admin/shared policy outcomes.
- Keep module-specific allowed action lists for later business milestones when `SearchClients`, `GetClientProfile`, report and catalog queries exist.
- Keep auth/permission technical logs for the next Milestone 2 step.

Validation:

- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Tests/BodyLife.Crm.Tests.csproj --configuration Release --nologo` passed with 11 core tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Web.Tests/BodyLife.Crm.Web.Tests.csproj --configuration Release --nologo` passed with 30 web authorization/request-context/query-permission tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 11 core tests, 30 web authorization/request-context/query-permission tests, 17 PostgreSQL infrastructure tests, 2 authenticated Playwright smoke tests and EF migration listing through `20260709143654_AddAccountCredentials`.
- `graphify update .` completed for code graph maintenance.
- `graphify . --update` was attempted for markdown progress/login documentation updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(users): add query permission results`.

Next recommended step:

- Add auth/permission technical logs with password/token masking.

## Step 21 - Auth and permission technical logs

Status: completed.

Scope:

- Add `IBodyLifeAuthTechnicalLogger` / `BodyLifeAuthTechnicalLogger` for structured auth technical events.
- Log login failures, successful logins and logout attempts from the existing `/Login` and `/Logout` Razor Page handlers.
- Add an ASP.NET Core `IAuthorizationMiddlewareResultHandler` that logs real server-side forbidden authorization results while leaving query permission hints as UI/advisory only.
- Include `event_name`, route, method, `request_correlation_id`, outcome/error category, account id, role/account type and session id where available.
- Omit or reduce sensitive and unnecessary personal values: no raw passwords, tokens, login names, display names or device labels in auth/permission logs.
- Add focused web tests for auth log fields and masking/omission behavior.
- Keep business audit, account management UI and module-specific command logging for later milestones.

Validation:

- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Web.Tests/BodyLife.Crm.Web.Tests.csproj --configuration Release --nologo` passed with 34 web auth/request-context/query-permission/logging tests.
- Initial `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` attempt passed build/core/web/infrastructure tests but hit one Playwright tablet wait timeout.
- Focused `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Ui.SmokeTests/BodyLife.Crm.Ui.SmokeTests.csproj --configuration Release --nologo` rerun passed with 2 authenticated Playwright smoke tests.
- Final `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, 11 core tests, 34 web tests, 17 PostgreSQL infrastructure tests, 2 authenticated Playwright smoke tests and EF migration listing through `20260709143654_AddAccountCredentials`.
- `graphify update .` completed for code graph maintenance.
- `graphify . --update` was attempted for markdown progress/login documentation updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(users): add auth technical logs`.

Next recommended step:

- Run Milestone 2 acceptance review and progress cleanup.

## Step 22 - Milestone 2 acceptance review

Status: completed.

Scope:

- Review Milestone 2 implementation against `docs/implementation-roadmap.md` acceptance criteria and test requirements.
- Create `docs/milestone-2-acceptance-review.md` with completed foundation, acceptance status, test coverage and follow-up gaps.
- Keep the review honest: Milestone 2 is not accepted as complete because Owner-managed named Admin/shared Reception/Admin account lifecycle and credentials are not implemented yet.
- Keep Milestone 3 blocked until those Milestone 2 gaps are closed.

Validation:

- `git diff --check` passed for the documentation changes.
- `graphify . --update` was attempted for markdown acceptance/progress updates but stopped because no semantic extraction API key/backend is configured.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, 11 core tests, 34 web tests, 17 PostgreSQL infrastructure tests, 2 authenticated Playwright smoke tests and EF migration listing through `20260709143654_AddAccountCredentials`.

Commit:

- `docs(users): review milestone 2 acceptance`.

Next recommended step:

- Add Owner-managed account lifecycle foundation for named Admin and shared Reception/Admin accounts.

## Step 23 - Staff account lifecycle foundation

Status: completed.

Scope:

- Add `StaffAccountLifecycleService` for Owner-managed named Admin and shared Reception/Admin account lifecycle.
- Require an Owner `CommandEnvelope` before lifecycle mutations; non-Owner actors receive permission denied results.
- Create only Admin-role `named_admin` or `shared_reception_admin` accounts; Owner account creation remains protected by the existing bootstrap workflow.
- Update staff account display names.
- Activate/deactivate staff accounts without hard delete; deactivation ends active sessions for the account.
- Add PostgreSQL-backed tests for create, permission denial, Owner protection, display-name updates, deactivate/reactivate behavior and session termination.
- Keep named Admin/shared credential setup, account-management UI, command routing and business audit for later steps.

Validation:

- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo` passed with 23 PostgreSQL infrastructure tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, 11 core tests, 34 web tests, 23 PostgreSQL infrastructure tests, 2 authenticated Playwright smoke tests and EF migration listing through `20260709143654_AddAccountCredentials`.
- `graphify update .` completed for code graph maintenance.
- `graphify . --update` was attempted for markdown progress/login-session/acceptance-review updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(users): add staff account lifecycle`.

Next recommended step:

- Add explicit credential setup/reset path for named Admin/shared Reception/Admin accounts without default credentials.

## Step 24 - Staff credential setup and reset foundation

Status: completed.

Scope:

- Add `StaffCredentialsService` for Owner-guarded credential setup and reset on named Admin and shared Reception/Admin accounts.
- Require an Owner `CommandEnvelope`; non-Owner actors receive permission denied without mutation.
- Protect Owner credentials behind the existing Owner bootstrap workflow and return stable not-found, validation and duplicate-login results.
- Trim and case-normalize login names, enforce the existing PostgreSQL unique constraint and store only PBKDF2 password hashes.
- Require explicit passwords of at least 12 characters; do not generate or seed default staff secrets.
- End active staff sessions atomically when credentials are reset.
- Allow credentials to be prepared for an inactive staff account while keeping normal login blocked until reactivation.
- Add PostgreSQL-backed integration tests for named Admin/shared Reception/Admin login, hash-only storage, reset/session revocation, Owner authorization, Owner protection, validation, duplicate login and inactive account rejection.
- Update login/session documentation and the Milestone 2 acceptance review. Milestone 2 remains open until an Owner-facing management command/surface, session-expiry coverage and account-management audit boundary are completed.

Validation:

- The first focused test attempt found a nullable test-helper compile mismatch; the second attempt passed 29 tests and exposed only the Npgsql `timestamptz` test assertion mapping. Both test-only issues were corrected.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo` passed with 30 PostgreSQL infrastructure tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 11 core tests, 34 web tests, 30 PostgreSQL infrastructure tests, 2 authenticated Playwright smoke tests and EF migration listing through `20260709143654_AddAccountCredentials`.
- `graphify update .` completed for code graph maintenance.
- `graphify . --update` was attempted for markdown progress/login-session/acceptance-review updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(users): add staff credential setup`.

Next recommended step:

- Add a minimal Owner-only account management command/surface that composes staff lifecycle and credential operations with visible results.

## Step 25 - Staff account business audit foundation

Status: completed.

Scope:

- Add append-only `bodylife.business_audit_entries` persistence with queryable actor/account/session/correlation fields, `jsonb` related/before/after summaries, entry-origin checks and entity/actor timeline indexes.
- Add `BusinessAuditAppender` as the shared infrastructure boundary that stages audit rows without committing independently.
- Append canonical lifecycle events for staff account create, display-name update, activate and deactivate operations.
- Append canonical credential events for explicit staff credential setup and reset without storing login names, raw passwords or password hashes in audit summaries.
- Persist each staff source mutation and its audit row in the same EF Core/PostgreSQL `SaveChanges` transaction and return the created `AuditEntryId` from successful mutation results.
- Keep permission-denied, validation, not-found, duplicate-login and already-active/inactive no-op outcomes audit-free.
- Add migration `20260709204232_AddBusinessAuditEntries` and review its idempotent SQL for `jsonb`, checks and descending timeline indexes.
- Add PostgreSQL tests for required audit context, canonical actions, audit ids, credential secret omission, denied/no-op/conflict behavior, database constraints and source/audit rollback.
- Update login/session documentation and Milestone 2 acceptance review. The Owner-facing account-management surface remains the next step.

Validation:

- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet /tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --configuration Release --nologo` passed with 0 warnings/errors before migration generation.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo` passed with 35 PostgreSQL infrastructure tests.
- `dotnet ef migrations script 20260709143654_AddAccountCredentials 20260709204232_AddBusinessAuditEntries --idempotent` generated review SQL with the expected audit table, `jsonb` summaries, checks and timeline indexes.
- The first full gate attempt stopped on the repository charset rule because EF generated UTF-8 BOM markers in the two new migration files; the markers were removed without changing migration content.
- Final `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 11 core tests, 34 web tests, 35 PostgreSQL infrastructure tests, 2 authenticated Playwright smoke tests and EF migration listing through `20260709204232_AddBusinessAuditEntries`.
- `graphify update .` completed for code graph maintenance with 2201 nodes and 2609 edges.
- `graphify . --update` was attempted for markdown progress/login-session/acceptance-review updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `build(audit): add staff account audit trail`.

Next recommended step:

- Add the minimal Owner-only account management Razor Page using the audited staff lifecycle and credential services.

## Step 26 - Owner staff account management surface

Status: completed.

Scope:

- Add `StaffAccountQueryService` to list canonical staff account type, active state, configured login and active-session count from PostgreSQL.
- Add the Owner-only `/Owner/StaffAccounts` Razor Page with create, display-name update, credential setup/reset and activate/deactivate forms.
- Authorize the entire `/Owner` folder with `BodyLife.OwnerOnly`; audited services repeat Owner authorization before every mutation.
- Create a fresh `CommandEnvelope` from authenticated account/session/correlation context for every POST.
- Use Post/Redirect/Get and `StaffAccountQueryService` after every action so the page renders canonical committed state rather than optimistic values.
- Require and audit a reason for credential reset and account deactivation; keep deactivation confirmation and active-session termination.
- Add shared busy/disabled form behavior to prevent repeat taps and show stable submit state.
- Add explicit `/AccessDenied` UI for authenticated Admin/shared sessions that attempt Owner-only navigation.
- Add Owner-only shell navigation while keeping the current account/session/device indicator visible.
- Add PostgreSQL query/reason tests and Playwright coverage for the complete Owner flow on tablet/phone, responsive overflow, busy-form wiring and named Admin denial.
- Update login/session documentation and Milestone 2 acceptance evidence. The Owner account-management criterion is now complete.

Validation:

- Focused `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo` passed with 38 PostgreSQL infrastructure tests.
- `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet /tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --configuration Release --nologo` passed with 0 warnings/errors.
- The first post-denial-state Playwright attempt passed 4 tests and hit one redundant `NetworkIdle` wait timeout after a completed login redirect. The new smoke test was changed to synchronize on URL and visible canonical results instead.
- Focused `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Ui.SmokeTests/BodyLife.Crm.Ui.SmokeTests.csproj --configuration Release --no-build --nologo` then passed with 5 Playwright tests twice consecutively.
- Final `DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 11 core tests, 34 web tests, 38 PostgreSQL infrastructure tests, 5 authenticated Playwright smoke tests and EF migration listing through `20260709204232_AddBusinessAuditEntries`.
- The installed Graphify/Python 3.14 default `forkserver` could not bind its local worker socket on this filesystem (`Errno 95`). Running the same structural rebuild with the supported `fork` multiprocessing mode completed with 2264 nodes and 2740 edges.
- `graphify . --update` was attempted for markdown progress/login-session/acceptance-review updates but stopped because no semantic extraction API key/backend is configured.

Commit:

- `feat(users): add owner staff management`.

Next recommended step:

- Add active-session expiry validation and PostgreSQL integration coverage, then rerun the Milestone 2 acceptance review.

## Step 27 - PostgreSQL-backed active session expiry

Status: completed.

Scope:

- Add canonical `expires_at` persistence to `bodylife.sessions` with a 12-hour sliding idle timeout shared by the database session and authentication cookie.
- Add migration `20260710093311_AddSessionExpiry` with an existing-row backfill from `last_seen_at`, a required expiry column, `expires_at > started_at` check and active account/expiry partial index.
- Validate every authenticated dynamic request against the PostgreSQL session and account before authorization.
- Reject and clear cookies for missing, ended, expired, claim-mismatched and inactive-account sessions; close expired/inactive session rows when detected.
- Renew `last_seen_at` and `expires_at` after successful activity while preserving the existing sliding-cookie behavior.
- Count staff sessions as active only while they are unended and unexpired.
- Add a structured `auth.session_rejected` technical event with stable result categories and no display name, device label, credential or token values.
- Add PostgreSQL tests for migration upgrade/backfill, expiry schema/index/constraint, login persistence, sliding renewal, exact-boundary expiry, ended-session rejection and canonical active-session counts.
- Add Playwright coverage proving a database-expired session returns to `/Login` and is closed in PostgreSQL.
- Update the session implementation contract in the data architecture and login/session documentation.

Validation:

- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --configuration Release --nologo` passed with 0 warnings/errors.
- The first focused PostgreSQL run passed 39 of 43 tests; the four failures were raw Npgsql `timestamptz` assertions expecting `DateTimeOffset` instead of the returned UTC `DateTime`. The test-only assertions were normalized without changing product behavior.
- Focused PostgreSQL validation then passed 44 tests, including migration backfill, session expiry behavior and claim-mismatch rejection.
- Focused web validation passed 35 tests, including secret-safe session rejection logging.
- Focused Playwright validation passed 6 authenticated tests, including PostgreSQL-backed session expiry and login redirect.
- Idempotent migration SQL from `20260709204232_AddBusinessAuditEntries` to `20260710093311_AddSessionExpiry` was reviewed and contains nullable add, 12-hour existing-row backfill, `not null`, expiry index and check constraint in the required order.
- Final `DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 11 core tests, 35 web tests, 44 PostgreSQL infrastructure tests, 6 authenticated Playwright smoke tests and EF migration listing through `20260710093311_AddSessionExpiry`.

Commit:

- `feat(users): validate active session expiry`.

Next recommended step:

- Re-run the Milestone 2 acceptance review against Steps 1-27; if all roadmap criteria are satisfied, close Milestone 2 before starting Milestone 3.
