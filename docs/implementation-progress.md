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

## Step 28 - Milestone 2 acceptance review

Status: completed; Milestone 2 accepted.

Scope:

- Re-audit every Milestone 2 roadmap task, acceptance criterion and required test area against ADR-012 and implementation evidence from Steps 14-27.
- Confirm that Step 27 closed the final explicit gap: PostgreSQL-backed active-session expiry and its integration coverage.
- Resolve the former partial permission-denied criterion for Milestone 2 using query permission results, service-level no-mutation behavior and the explicit Owner-only denied UI covered by Playwright.
- Mark account lifecycle, staff credential management, account-management audit and session validation complete for the milestone rather than backend-only foundations.
- Preserve future command obligations for server authorization, actor/session context, business audit and canonical rereads as cross-cutting guardrails, not unfinished Milestone 2 scope.
- Record that Milestone 2 acceptance is not a production-readiness claim; deployment hardening, backup retention evidence and restore rehearsal remain later roadmap gates.
- Authorize transition to Milestone 3 without changing code or reopening accepted architecture decisions.

Validation:

- Reviewed `docs/implementation-roadmap.md` Milestone 2, ADR-012, `docs/architecture-baseline.md`, the prior acceptance review and implementation progress through Step 27.
- Rechecked concrete policy, request-context, logging, PostgreSQL and Playwright evidence; no unresolved Milestone 2 blocker remains.
- `DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 11 core tests, 35 web tests, 44 PostgreSQL infrastructure tests, 6 authenticated Playwright smoke tests and EF migration listing through `20260710093311_AddSessionExpiry`.
- `graphify . --update` was attempted for the acceptance/progress documentation change but stopped because no semantic extraction API key/backend is configured; no graph artifact changed.

Commit:

- `docs(users): accept milestone 2`.

Next recommended step:

- Start Milestone 3 with only the Clients/Search normalization contract and focused domain tests for card, phone, name and last-four phone values; defer schema, commands and UI to following steps.

## Step 29 - Clients/Search normalization contract

Status: completed.

Scope:

- Add the first Milestone 3 domain implementation inside the owning `Clients/Search` module without starting persistence, commands or UI.
- Define one deterministic `ClientSearchNormalizer` for card number, phone, phone last four, name parts and normalized full name.
- Normalize card values with Unicode NFKC, whitespace removal and invariant casing while preserving leading zeroes and non-whitespace punctuation.
- Normalize phone values to ASCII digits from accepted formatting characters, preserve leading zeroes, reject unsupported content and avoid unapproved country-code inference.
- Extract `phone_last4` as the exact final four digits and require at least four normalized digits.
- Normalize names with Unicode NFC, collapsed whitespace, invariant casing and canonical apostrophe/dash variants without transliteration, diacritic removal or fuzzy matching.
- Keep raw identity values separate for future display/audit and document that persistence, commands and queries must reuse this contract rather than duplicate formulas.
- Add focused domain tests for card, phone, last-four and name edge cases, including Unicode compatibility and Ukrainian casing.
- Link the detailed normalization contract from the data architecture.

Validation:

- Focused `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Tests/BodyLife.Crm.Tests.csproj --configuration Release --nologo` passed with 34 domain/application tests.
- The first full gate passed build/analyzers plus all core, web and PostgreSQL tests, then hit one existing `NetworkIdle` timeout in the tablet reception smoke after the login redirect had completed; no Clients/Search code participates in that wait.
- Immediate focused Playwright rerun passed all 6 tests in 7 seconds, confirming a transient harness wait rather than a normalization regression.
- Final `DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 44 PostgreSQL infrastructure tests, 6 authenticated Playwright smoke tests and EF migration listing through `20260710093311_AddSessionExpiry`.
- No migration check was added because this step intentionally introduces no persistence change.

Commit:

- `feat(clients): add search normalization contract`.

Next recommended step:

- Add only the PostgreSQL schema foundation for `clients` and historical/current `client_card_assignments`, including normalized fields, constraints, partial unique indexes, migration review and PostgreSQL tests; defer duplicate-warning persistence, commands, search queries and UI.

## Step 30 - Clients/Search PostgreSQL storage foundation

Status: completed.

Scope:

- Add EF Core persistence records and mappings for `bodylife.clients` and historical/current `bodylife.client_card_assignments` inside the owning Clients/Search infrastructure folder.
- Persist raw identity values separately from the normalized full-name, phone and card values defined by the Step 29 normalization contract.
- Allow a client to exist without a phone or card while requiring a complete, internally consistent normalized phone tuple whenever a phone is present.
- Preserve card assignment history with explicit assignment/end actor and timestamp metadata; require complete end metadata for every historical assignment.
- Enforce one current card per client and one current client per normalized card number with PostgreSQL partial unique indexes.
- Add search-supporting indexes for normalized full name, normalized phone, phone last four and card history.
- Add migration `20260710111409_AddClientsSearchStorage` and review its idempotent PostgreSQL SQL.
- Add PostgreSQL tests for schema objects, optional phone/card behavior, phone tuple constraints, current-card uniqueness, historical reassignment, lifecycle metadata and concurrent assignment conflict.
- Keep duplicate-warning acknowledgement persistence, commands, search queries, audit workflows and UI outside this step.

Validation:

- The first migration generation used stale `--no-build` startup output and produced an empty migration; it was removed with EF tooling and regenerated only after rebuilding the current model.
- Migration review found PostgreSQL `CHECK` null semantics could admit incomplete phone or card-end metadata; the checks were made explicitly null-safe before the final migration was generated.
- Focused `DOTNET_ROOT=/tmp/bodylife-dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo` passed with 52 PostgreSQL infrastructure tests.
- Idempotent migration SQL from `20260710093311_AddSessionExpiry` to `20260710111409_AddClientsSearchStorage` was reviewed and contains only the expected two tables, null-safe checks, restrictive foreign keys, search/history indexes and both current-card partial unique indexes.
- Final `DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 52 PostgreSQL infrastructure tests, 6 authenticated Playwright smoke tests and EF migration listing through `20260710111409_AddClientsSearchStorage`.
- The structural Graphify rebuild completed with 2439 nodes and 3060 edges.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction API key/backend is configured.

Commit:

- `feat(clients): add identity storage foundation`.

Next recommended step:

- Add the optional `duplicate_warning_acknowledgements` PostgreSQL persistence and focused constraint tests needed for the later atomic CreateClient/UpdateClient warning-override workflow; continue to defer commands, search queries and UI.

## Step 31 - Duplicate warning acknowledgement storage

Status: completed.

Scope:

- Add the optional `bodylife.duplicate_warning_acknowledgements` source-fact table required by the Milestone 3 CreateClient/UpdateClient transaction contract.
- Persist the target client, matched client, warning type, acknowledging account, acknowledgement timestamp and required reason without duplicating business audit fields.
- Restrict warning types to `duplicate_phone` and `similar_name`, require different target/matched clients and reject blank reasons at the PostgreSQL boundary.
- Use restrictive foreign keys to both clients and the acknowledging account so acknowledgement history cannot be orphaned by deletes.
- Add target-client and matched-client timeline indexes plus an acknowledging-account FK index; deliberately allow repeated acknowledgements for later update workflows.
- Add migration `20260710113814_AddDuplicateWarningAcknowledgements` and review its idempotent PostgreSQL SQL.
- Add PostgreSQL tests for schema objects, restrictive FKs, both warning types, repeated acknowledgements, unknown warning type, self-match, blank reason and matched-client deletion protection.
- Keep duplicate candidate detection, CreateClient/UpdateClient commands, business audit composition, search queries and UI outside this step.

Validation:

- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --configuration Release --nologo` passed with 0 warnings/errors before migration generation.
- Focused `DOTNET_ROOT=/tmp/bodylife-dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo` passed with 58 PostgreSQL infrastructure tests.
- Idempotent migration SQL from `20260710111409_AddClientsSearchStorage` to `20260710113814_AddDuplicateWarningAcknowledgements` was reviewed and contains only the expected table, three null-safe checks, three restrictive foreign keys and three indexes.
- The first full validation attempt stopped on formatter-only indentation findings in two composite index expressions; no build or test failed. The formatting was corrected and `dotnet format --verify-no-changes` then passed.
- Final `DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 58 PostgreSQL infrastructure tests, 6 authenticated Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- The structural Graphify rebuild completed with 2480 nodes and 3153 edges.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction API key/backend is configured.

Commit:

- `feat(clients): persist duplicate warning acknowledgements`.

Next recommended step:

- Add a read-only PostgreSQL duplicate-candidate query contract for exact normalized phone and conservative normalized-name matches, including self-exclusion for future updates and focused tests; defer CreateClient/UpdateClient mutation, audit and UI to following steps.

## Step 32 - Client duplicate candidate query

Status: completed.

Scope:

- Add a typed `FindClientDuplicateCandidatesQuery` public contract in the owning Clients/Search module using the established application query conventions.
- Normalize raw proposed identity values with the canonical `ClientSearchNormalizer` before querying PostgreSQL.
- Define `duplicate_phone` behavior as exact normalized-phone equality when a phone is present.
- Define conservative v1 `similar_name` behavior as exact normalized-full-name equality without prefix, fuzzy, phonetic, transliteration or reordered-name matching.
- Return one typed candidate per matched client and warning type, allowing one client to produce both phone and name warnings.
- Include inactive clients and raw display identity in candidate summaries while keeping normalized persistence values internal.
- Support optional target-client exclusion for future UpdateClient checks.
- Implement the query with EF Core `AsNoTracking`, register its `IBodyLifeQueryHandler` in DI and keep it read-only with no acknowledgement or business-audit writes.
- Add PostgreSQL integration tests for combined matches, inactive candidates, canonical raw-input normalization, self-exclusion, absent phone, exact-only names, normalizer validation and no-write behavior.
- Keep CreateClient/UpdateClient mutation, acknowledgement persistence orchestration, audit composition and UI outside this step.

Validation:

- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --configuration Release --nologo` passed with 0 warnings/errors after adding the query contract and handler.
- Focused `DOTNET_ROOT=/tmp/bodylife-dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' /tmp/bodylife-dotnet/dotnet test tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj --configuration Release --nologo` passed with 63 PostgreSQL infrastructure tests.
- Final `DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 63 PostgreSQL infrastructure tests, 6 authenticated Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- No migration was generated because this step adds only a read-only query over the existing normalized indexes.
- The structural Graphify rebuild completed with 2514 nodes and 3237 edges.
- `graphify . --update` was attempted for the normalization/progress documentation changes but stopped because no semantic extraction API key/backend is configured.

Commit:

- `feat(clients): add duplicate candidate query`.

Next recommended step:

- Implement the server-side CreateClient command without UI: Admin/Owner authorization, canonical normalization, optional current card, duplicate-warning acknowledgement validation/persistence, idempotency, one PostgreSQL transaction, `client.created` business audit and a canonical reread target.

## Step 33 - CreateClient command workflow

Status: completed.

Scope:

- Add the typed `CreateClientCommand`, `ClientOperationalStatus` and duplicate-warning acknowledgement input contracts inside the owning Clients/Search module.
- Enforce Owner, named Admin and shared Reception/Admin actor shapes plus canonical active account/session checks before mutation.
- Normalize name, optional phone and optional card values with the accepted `ClientSearchNormalizer`; preserve trimmed raw display values separately.
- Require an idempotency key and store only a SHA-256 request fingerprint, actor/session context and canonical result references in `command_idempotency_keys`.
- Replay a completed identical request with the original client/audit ids and reject reuse of the key for a changed payload.
- Run card availability, duplicate-candidate lookup and exact acknowledgement-set validation inside a serializable PostgreSQL transaction.
- Return `duplicate_warning_not_acknowledged` for every missing phone/name warning, reject stale/extra/duplicate acknowledgements and persist accepted reasons as source facts.
- Create the client, optional current card assignment, warning acknowledgements, succeeded idempotency record and `client.created` business audit through one `SaveChanges` and transaction commit.
- Include actor/account/session, entry origin, occurred/recorded times, correlation/idempotency keys, raw identity/card summary, warning summary and related ids in audit.
- Return the new client as both primary entity and canonical reread target; Memberships recalculation is correctly not involved.
- Map current-card uniqueness races and nested PostgreSQL serialization/deadlock failures to stable card/concurrency command errors, clearing rolled-back tracked state.
- Register the command handler through the existing `IBodyLifeCommandHandler` convention.
- Keep UpdateClient, AssignOrChangeCard, client profile/search UI and all htmx work outside this step.

Validation:

- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --configuration Release --nologo` passed with 0 warnings/errors after adding the command contract and handler.
- The first focused test attempt stopped before execution on two xUnit analyzer findings for `Where(...)+Assert.Single`; the assertions were changed to the predicate overload.
- The next focused run passed 8 of 9 tests and exposed that Npgsql wraps a PostgreSQL `40001` serialization failure in `InvalidOperationException -> DbUpdateException`; known PostgreSQL failures are now found through the exception chain while unknown exceptions still rethrow.
- Focused CreateClient validation then passed 9 tests covering all accepted actor kinds, denied canonical actors, optional card/phone, normalization, duplicate-warning acknowledgement sets, audit/idempotency atomicity, input validation, card conflict, replay/key reuse, concurrent card assignment and paper-fallback timestamps.
- Full PostgreSQL infrastructure validation passed with 72 tests.
- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal --no-restore` passed.
- Final `DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 72 PostgreSQL infrastructure tests, 6 authenticated Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- No migration was generated because the command uses the existing client, card, acknowledgement, audit and idempotency schema.
- The structural Graphify rebuild completed with 2595 nodes and 3503 edges.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction API key/backend is configured.

Commit:

- `feat(clients): add create client command`.

Next recommended step:

- Implement the server-side UpdateClient command without UI or card mutation: expected `updated_at` stale-state guard, canonical normalization, duplicate-warning acknowledgement validation/persistence, authorization, idempotency, one transaction, `client.updated` before/after audit and client reread target.

## Step 34 - UpdateClient command workflow

Status: completed.

Scope:

- Add the typed `UpdateClientCommand` contract inside the owning Clients/Search module with client id, expected `updated_at`, editable identity/contact/status fields and duplicate-warning acknowledgement inputs.
- Keep current-card assignment outside this command so profile edits cannot silently assign, replace or clear a card.
- Enforce Owner, named Admin and shared Reception/Admin actor shapes plus canonical active account/session checks before mutation.
- Require a non-empty client id, expected `updated_at`, idempotency key and valid canonical identity input.
- Compare the caller's expected `updated_at` with canonical PostgreSQL state and return `stale_state` before any mutation when the profile changed after form load.
- Normalize name and optional phone through `ClientSearchNormalizer`, preserve trimmed raw display values and support active/inactive operational status changes.
- Exclude the edited client from duplicate detection, require the exact current phone/name warning acknowledgement set and persist accepted acknowledgement reasons.
- Reject a no-op update unless it records valid duplicate-warning acknowledgements.
- Run canonical actor validation, idempotency replay, client load, stale guard, duplicate checks, profile update, acknowledgement persistence, audit and idempotency persistence in one serializable PostgreSQL transaction.
- Write one `client.updated` business audit entry with before/after identity summaries, actor/session context and related acknowledgement/matched-client ids.
- Return the updated client as both primary entity and canonical reread target; Memberships recalculation is correctly not involved.
- Preserve an existing current card byte-for-byte and map nested PostgreSQL serialization/deadlock failures to the stable concurrency command error.
- Extract the shared, already-proven client command validation, normalization, authorization, idempotency, conflict mapping and result helpers from `CreateClientCommandHandler` into internal Clients/Search infrastructure support.
- Register the UpdateClient handler through the existing `IBodyLifeCommandHandler` convention and keep UI work outside this step.

Validation:

- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --configuration Release --nologo` passed with 0 warnings/errors after adding the command contract and handler.
- The first connected focused PostgreSQL run passed 7 of 8 tests; the only failure showed that local `067...` and international `+38...` inputs are deliberately different under the accepted no-country-code-inference normalization contract. The duplicate test fixture was corrected without changing product behavior.
- Focused UpdateClient PostgreSQL validation then passed 8 tests covering accepted and denied actors, normalization/status update, unchanged current card, missing/stale/no-op failures, exact duplicate acknowledgements with self-exclusion, input validation, idempotent replay/key reuse and concurrent editing.
- Focused CreateClient regression validation passed all 9 tests after extracting shared client command support.
- Full PostgreSQL infrastructure validation passed with 80 tests and no skips.
- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal --no-restore` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 80 PostgreSQL infrastructure tests, 6 authenticated Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- No migration was generated because the command uses the existing client, acknowledgement, audit and idempotency schema.
- `graphify update .` completed the structural rebuild with 2654 nodes, 3703 edges and 450 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(clients): add update client command`.

Next recommended step:

- Implement the server-side `AssignOrChangeCard` command without UI: lock the target client/current assignment, normalize and validate the requested card, require a reason when replacing or clearing, preserve assignment history, enforce current-card uniqueness under concurrency, add idempotency and `card.assigned`/`card.changed`/`card.cleared` audit, and return the client reread target.

## Step 35 - AssignOrChangeCard command workflow

Status: completed.

Scope:

- Add the typed `AssignOrChangeCardCommand` contract inside the owning Clients/Search module with client id, expected current assignment id, optional new card number and explicit clear-card intent.
- Treat a null expected assignment id as "no current card was loaded" and require a supplied id to match the row locked from canonical PostgreSQL state; return `stale_state` instead of overwriting a newer assignment.
- Enforce Owner, named Admin and shared Reception/Admin actor shapes plus canonical active account/session checks before mutation.
- Extract reusable client-command envelope validation and normalized idempotency metadata without changing the accepted CreateClient/UpdateClient identity or fingerprint contracts.
- Require an idempotency key, correlation id and valid entry-origin metadata; normalize semantic card payloads before fingerprinting so formatting-equivalent retries replay the original result.
- Require exactly one intent: provide a non-empty normalized new card number or explicitly clear the current card.
- Require a reason or command comment when replacing, reissuing or clearing an existing card; first assignment to a client without a card does not require a reason.
- Reject a backdated change whose `occurred_at` precedes the current assignment time so historical lifecycle constraints remain explainable.
- Run canonical actor validation, idempotency replay, target-client lock, current-assignment lock, stale validation, current-card conflict check, history mutation, audit and idempotency persistence in one serializable PostgreSQL transaction.
- Use PostgreSQL `FOR UPDATE` on the target client and current assignment rows, plus the existing partial unique indexes as the final cross-client and one-current-row concurrency guard.
- End the previous assignment with complete event time, actor and reason metadata; create a new current source row when assigning/changing and preserve history when clearing.
- Support explicit same-normalized-number reissue by ending the old row before inserting its replacement while keeping both writes inside the same transaction.
- Write `card.assigned`, `card.changed` or `card.cleared` business audit with old/new assignment summaries, actor/session, reason/comment and related assignment ids.
- Return the client as both primary entity and canonical reread target; Memberships recalculation is correctly not involved.
- Register the command handler through the existing `IBodyLifeCommandHandler` convention and keep migrations, SearchClients, profile queries and all UI outside this step.

Validation:

- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --configuration Release --nologo` passed with 0 warnings/errors after the contract, shared envelope support and handler were added.
- Focused AssignOrChangeCard PostgreSQL validation passed 11 tests covering all accepted and denied actor kinds, assign/change/clear history, all three audit events, same-number reissue, missing/invalid/stale/reason/occurred-at failures, occupied-card rollback including a conflict after the previous row was already ended inside the transaction, replay/key reuse, paper-fallback timestamps and both same-client and cross-client concurrency.
- Focused CreateClient and UpdateClient regression validation passed all 17 tests after extracting common envelope validation/idempotency metadata.
- Full PostgreSQL infrastructure validation passed with 91 tests and no skips.
- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal --no-restore` passed.
- The first post-review full gate passed build/analyzers, 34 core, 35 web, 91 PostgreSQL and 5 of 6 Playwright tests, then hit the existing tablet `NetworkIdle` timeout after the event had fired; no Clients/Search UI code changed in this step.
- Immediate focused Playwright rerun passed all 6 tests in 7 seconds.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 91 PostgreSQL infrastructure tests, 6 authenticated Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- No migration was generated because the command uses the existing client, historical/current card, audit and idempotency schema.
- `graphify update .` completed the structural rebuild with 2713 nodes, 3947 edges and 451 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(clients): add card assignment command`.

Next recommended step:

- Implement the read-only PostgreSQL `SearchClients` query without UI or profile composition: exact current-card priority and unique auto-open target, bounded partial/ambiguous card results, normalized name/phone/last-four matching, inactive-client visibility, deterministic ordering and no business-audit writes.

## Step 36 - SearchClients query workflow

Status: completed.

Scope:

- Add a typed `SearchClientsQuery` public contract in the owning Clients/Search module with actor/session context, search text, `auto`/`card`/`name`/`phone`/`last4` mode, inactive-client flag, bounded limit and page cursor.
- Add a structured query result with success/permission/validation status, compact client rows, optional exact-card auto-open target, next cursor and stable error metadata.
- Return display identity, raw phone/current-card values, typed operational status, match type/priority, Clients-owned warnings and a nullable Memberships summary placeholder without calculating membership state.
- Enforce Owner, named Admin and shared Reception/Admin actor shapes plus canonical active account/session state before reading reception data; denied searches return no rows.
- Keep session/account rows unchanged during search authorization and create no business audit or idempotency records for this read-only query.
- Normalize each explicit mode with `ClientSearchNormalizer`; `auto` evaluates canonical card/name and valid phone/last-four terms together without fuzzy, transliteration or country-code inference.
- Join only current card assignments, so historical/ended cards never match or auto-open.
- Rank exact current card first, followed by exact phone, phone last four, exact full name, partial card, partial phone and partial name; break ties by active status, normalized full name and client id.
- Return `auto_open_client_id` only for one exact current-card match in `auto` or `card` semantics; partial card and all name/phone/last-four matches always remain selectable lists.
- Support conservative substring matching over normalized card/name/phone values for the small one-gym v1 dataset while retaining existing equality/search indexes; do not add trigram/fuzzy infrastructure before evidence requires it.
- Exclude inactive clients by default, include them only when requested, and return server-owned `client_inactive` and `no_current_card` warnings where applicable.
- Use a deterministic bounded offset cursor with limits from 1 through 50 and a maximum accepted offset of 10,000.
- Implement the EF Core query with `AsNoTracking`, register its `IBodyLifeQueryHandler`, and keep GetClientProfile, Memberships composition, Razor Pages and htmx outside this step.

Validation:

- The first build stopped on a nullable analyzer finding inside the guarded phone LINQ expression; explicit null-state markers were added without changing translated SQL semantics.
- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --configuration Release --nologo` then passed with 0 warnings/errors.
- The first focused PostgreSQL run passed 8 of 9 tests; the only failure was a test-only attempt to execute three actor checks concurrently through one scoped EF Core `DbContext`. Those role checks were made sequential, matching scoped request usage, without changing product behavior.
- Focused SearchClients PostgreSQL validation then passed 9 tests covering accepted/denied actors, no session/business writes, exact/current/historical card behavior, partial ambiguity, name/phone/last-four ranking, inactive warnings, stable pagination, validation failures and empty success.
- Full PostgreSQL infrastructure validation passed with 100 tests and no skips.
- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal --no-restore` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 100 PostgreSQL infrastructure tests, 6 authenticated Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- No migration was generated because the query reuses the existing normalized client and current-card indexes; no fuzzy/trigram index is justified for this bounded v1 search step.
- `graphify update .` completed the structural rebuild with 2776 nodes, 4112 edges and 473 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(clients): add client search query`.

Next recommended step:

- Implement the read-only `GetClientProfile` shell without UI: authorized canonical client identity, current assignment id/card, operational status, `updated_at`, empty/nullable Memberships area placeholder, Clients-owned warnings and server-provided allowed-action permission results; defer history aggregation and Razor/htmx rendering.

## Step 37 - GetClientProfile query shell

Status: completed.

Scope:

- Add a typed `GetClientProfileQuery` public contract in the owning Clients/Search module with actor/session context, client id, optional membership as-of date and explicit future history/drill-down composition flags.
- Add a structured query result with success, permission, not-found and validation statuses plus stable error metadata.
- Return canonical raw identity fields, display name, optional phone/comment, typed operational status, creation time and `updated_at` version for later stale-form protection.
- Return only the current card assignment id, raw card number and assignment time; historical card rows are deliberately excluded from current profile state.
- Add an explicit empty Memberships-owned area with nullable current summary, timeline and warning placeholders without calculating active status, remaining visits, dates or any other Memberships formula.
- Reuse Clients-owned `client_inactive` and `no_current_card` warnings between SearchClients and GetClientProfile through narrow shared query support.
- Generalize the compact membership and warning DTO names so both Clients queries can expose the same future Memberships composition contract without duplicating types.
- Return server-provided permission results only for the two implemented profile mutations, `clients.update` and `clients.assign_or_change_card`, under the accepted Admin-or-Owner policy; mutation handlers still reauthorize canonical actor/session state.
- Enforce Owner, named Admin and shared Reception/Admin actor shapes plus canonical active account/session state before reading profile data.
- Reject unsupported history and drill-down composition flags explicitly until the owning modules expose those reads.
- Implement the EF Core query with `AsNoTracking`, register its `IBodyLifeQueryHandler`, and keep it read-only with no session touch, business audit or idempotency writes.
- Add a canonical reread integration test that executes the real UpdateClient and AssignOrChangeCard commands and then proves the profile returns committed identity, version and current-card state.
- Keep Memberships composition, recent activity/history aggregation, Razor Pages, htmx and mutation forms outside this step.

Validation:

- The first build stopped on a local LINQ range-variable name collision in the profile projection; the projected card variable was renamed without changing query semantics.
- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet build BodyLife.Crm.sln --configuration Release --nologo` passed with 0 warnings/errors.
- Focused GetClientProfile PostgreSQL validation passed 6 tests covering all accepted actor kinds, denied canonical actors, canonical identity/card/version reads, Clients warnings, exclusion of historical cards, stable validation/not-found results, command-to-profile canonical reread and no query writes.
- Focused SearchClients regression validation passed all 9 tests after extracting shared query support and generalizing the compact DTO names.
- Full PostgreSQL infrastructure validation passed with 106 tests and no skips.
- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity diagnostic` passed and formatted 0 files.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 106 PostgreSQL infrastructure tests, 6 authenticated Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- No migration was generated because the profile query reads the existing client and current-card schema.
- `graphify update .` completed the structural rebuild with 2834 nodes, 4260 edges and 471 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(clients): add client profile query`.

Next recommended step:

- Add the first server-rendered reception UI read path: a tablet-first/phone-safe search form and htmx result states backed by `SearchClients`, with exact-card auto-open into the read-only `GetClientProfile` shell; defer profile mutation forms and broader visual polish.

## Step 38 - Reception search and profile read UI

Status: completed.

Scope:

- Replace the placeholder Reception Razor Page with the first operational Milestone 3 UI exemplar instead of a landing page or generic CRUD screen.
- Resolve the authenticated actor/session through the existing request-context service and invoke the typed `SearchClients` and `GetClientProfile` query handlers directly from the Razor PageModel.
- Keep a progressive full-page GET path for search/profile reads while adding dedicated htmx handlers that return only the stable reception workspace or profile fragment.
- Return canonical `HX-Push-Url` values after htmx reads so browser history and hard refreshes use ordinary full-page URLs rather than fragment-handler URLs.
- Add one compact search form for card, name, phone and phone-last-four modes, plus the optional inactive-client checkbox and a canonical clear action.
- Add visible search loading state, request synchronization with `hx-sync=replace`, disabled in-flight search submission and stable outerHTML targets for stale-response protection.
- Render initial, validation/permission error, no-match, exact-card and multiple-result states from server query results.
- Render compact touch-selectable result rows with raw identity/card/phone values, typed match/status labels, nullable server membership summaries and server-provided Clients warnings.
- Auto-open a profile only from canonical `auto_open_client_id`; partial/ambiguous searches keep the profile empty until the user selects a result.
- Render the read-only canonical profile identity, current card, phone, `updated_at`, optional reception note, Clients warnings and empty/current server membership area without adding formulas to Razor or JavaScript.
- Use a two-area tablet layout and one-column phone layout with visible warnings, at least 44px controls and no horizontal overflow; keep profile mutation forms, recent history, reports and broad visual polish outside this step.
- Self-host pinned `htmx.org` 2.0.10 and its 0BSD license under Web static assets so the reception workflow does not depend on a production CDN or frontend build system.
- Preserve the existing Owner/named Admin/shared Reception authorization boundary on the `/Reception` Razor folder; query handlers continue to revalidate canonical account/session state.
- Seed deterministic test-only clients in each isolated UI smoke PostgreSQL database, including exact-card, ambiguous-name, no-card and inactive cases.
- Extend Playwright coverage across 1024x768 tablet and 390x844 phone viewports for real htmx search/profile requests, canonical URLs, exact-card auto-open, ambiguous no-auto-open, explicit profile selection, visible warnings, no-match state and horizontal-overflow checks.
- Add a JavaScript-disabled Playwright case proving the same exact-card search/profile path works through ordinary server-rendered GET navigation.
- Add an opt-in `BODYLIFE_UI_SCREENSHOT_DIR` hook for local visual QA without creating CI artifacts by default.

Validation:

- The first PageModel build stopped on C# inference between `null` and an enum in optional route values; explicit nullable casts fixed the compile-only issue.
- Release solution build passed with 0 warnings/errors after the PageModel, Razor partials, CSS, self-hosted htmx and smoke fixtures were added.
- The self-hosted htmx file matches the official 2.0.10 npm package with SHA-384 `1f94ab71fca01e602e4c366984c1ea0492dcdc586cb0a8c6ef0fc2782a4545e49fc015834caa64ccf3fc73e70bb0af95`.
- The first focused Playwright run passed JavaScript-disabled fallback and session-expiry cases but both htmx viewport cases asserted browser history before htmx settled; the test now waits for `.htmx-request` completion without changing product behavior.
- Focused Reception Playwright validation then passed 4 tests: tablet and phone search/profile workflows, JavaScript-disabled full-page fallback and expired-session enforcement.
- Opt-in visual capture passed both viewport workflows and produced four full-page screenshots for exact-profile and multiple-results states; visual inspection found no clipped text, incoherent overlap or horizontal overflow. A too-tall phone empty-profile state was tightened and rechecked.
- Full UI smoke validation passed 7 tests, including the existing Owner staff-management and named-Admin denial regressions.
- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity diagnostic` passed and formatted 0 files.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 106 PostgreSQL infrastructure tests, 7 authenticated/progressive-fallback Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- No migration was generated because the step adds only server-rendered UI, htmx static assets and isolated smoke-test seed data.
- `graphify update .` completed the structural rebuild with 2974 nodes, 4781 edges and 466 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(ui): add reception search and profile read path`.

Next recommended step:

- Add the permission-aware `UpdateClient` Razor/htmx action from the profile: expected-`updated_at` stale guard, duplicate-candidate warning review and exact acknowledgement inputs, command execution, inline errors and canonical `GetClientProfile` reread; keep card management and create-client UI for separate following steps.

## Step 39 - Reception UpdateClient UI workflow

Status: completed.

Scope:

- Add a permission-aware `UpdateClient` action to the existing server-rendered client profile only when `GetClientProfile` returns the `clients.update` allowed action.
- Keep card assignment outside the identity form so card lifecycle changes continue exclusively through the separate audited `AssignOrChangeCard` command.
- Post the canonical client id and expected `updated_at` version together with a fresh idempotency key and the preserved reception search context.
- Resolve the authenticated actor/session through the existing request-context service and invoke the typed `UpdateClient` command handler without adding direct persistence or business rules to the PageModel.
- Re-query duplicate candidates only after the command reports a duplicate acknowledgement error; render only the current server candidates and bind acknowledgements to exact matched-client/warning-type pairs.
- Require an explicit checkbox and reason for every duplicate warning while relying on the command to revalidate the full warning set inside its PostgreSQL transaction.
- Render command validation and duplicate errors inside the edit action without replacing the canonical profile shown above it.
- On stale/concurrency errors, discard submitted business fields, reread the canonical profile, regenerate the expected version/idempotency key and reopen the form with the conflict message.
- After success, reread the complete reception workspace so the client header, profile details, warnings, allowed actions and current search-result row all reflect committed canonical state.
- Preserve progressive non-htmx POST/redirect behavior while using stable htmx targets, canonical `HX-Push-Url`, in-flight request dropping and busy/disabled submission states for the interactive path.
- Add tablet and phone styling for identity fields, duplicate review blocks, acknowledgement controls, inline errors and action footer without hiding warnings or introducing horizontal overflow.
- Extend the isolated Playwright PostgreSQL fixture with distinct tablet, phone, stale-target and duplicate-candidate clients plus direct evidence queries for `client.updated` audit, UpdateClient idempotency and duplicate acknowledgement records.
- Add Playwright coverage for tablet/phone duplicate-warning rejection and exact acknowledgement, canonical workspace reread, audit/idempotency evidence, stale-form canonical refresh and retry, and observed disabled submit state during the htmx request.
- Keep CreateClient UI, card management UI, Memberships composition and database migrations outside this step.

Validation:

- Release UI smoke project build passed with 0 warnings/errors.
- The first focused reception run passed 5 of 7 tests; the two failures exposed shared duplicate fixture data and an over-broad strict locator. Distinct candidates and a scoped locator fixed only the test setup, after which all 7 reception tests passed.
- A later busy-state assertion sampled the button after the response DOM swap and failed the three mutation cases; the test now begins observing the disabled transition before click while delaying only intercepted UpdateClient requests. The final focused reception run passed all 7 tests in 19 seconds.
- Opt-in visual capture produced tablet and phone duplicate-review and success screenshots. Inspection confirmed readable warning/action order, canonical result/profile updates, no clipped controls and no horizontal overflow at 1024x768 and 390x844.
- Full Playwright smoke validation passed all 10 tests, including existing authentication, authorization, reception search/profile and JavaScript-disabled fallback regressions.
- Focused PostgreSQL UpdateClient command regression validation passed all 8 tests.
- `DOTNET_ROOT=/tmp/bodylife-dotnet /tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity diagnostic --no-restore` passed and formatted 0 of 204 files.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 106 PostgreSQL infrastructure tests, 10 Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- Chromium installation was skipped only because the browser was already installed; the full Playwright suite itself ran successfully.
- No migration was generated because this step adds only Razor/htmx UI composition, client-side busy-state recovery and isolated smoke-test evidence data.
- `graphify update .` completed the structural rebuild with 3013 nodes, 4928 edges and 467 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(ui): add client update workflow`.

Next recommended step:

- Add the permission-aware `AssignOrChangeCard` Razor/htmx action from the profile: post the expected current assignment id, use explicit assign/change/clear intent, require a reason for replacement or clearing, render occupied-card/stale errors inline, and reread the complete workspace so exact-card search and profile state agree after commit. Keep CreateClient UI as a separate following step.

## Step 40 - Local migration drift guard

Status: completed.

Scope:

- Diagnose the local Owner login failure against the actual Docker development database instead of changing authentication or session business logic.
- Confirm that the database had stopped at `20260709143654_AddAccountCredentials` while four later migrations were pending, including `20260710093311_AddSessionExpiry`, which owns the missing `sessions.expires_at` column.
- Stop the web process and apply all four existing forward EF Core migrations to the development database without reset, direct schema patches or loss of the existing Owner credential.
- Extend PostgreSQL readiness beyond connectivity so `/health/ready` reports unhealthy whenever the configured database has pending EF Core migrations.
- Include the stable health-check description in the JSON response so connection failure and schema drift are distinguishable without exposing connection strings or secrets.
- Add a PostgreSQL-backed readiness regression that migrates a disposable database only to the pre-session-expiry state and expects `503 Service Unavailable`, while preserving the existing fully migrated `200 OK` case.
- Add `scripts/apply-migrations.sh` as the explicit local/development EF Core migration command using normal application configuration and environment overrides.
- Document the local `Docker up -> apply migrations -> run app` order and the requirement to rerun migrations after pulling schema changes.
- Keep application startup migration-free and retain the accepted explicit/reviewable migration discipline for non-development deployment.

Validation:

- Connected EF migration listing initially showed `AddBusinessAuditEntries`, `AddSessionExpiry`, `AddClientsSearchStorage` and `AddDuplicateWarningAcknowledgements` as pending.
- `dotnet-ef database update` applied those four existing migrations successfully to `bodylife_crm_dev`; a second connected migration listing showed all eight migrations applied.
- PostgreSQL metadata confirmed `bodylife.sessions.expires_at` is non-nullable `timestamp with time zone` after the update.
- The active Owner credential count remained unchanged after migration; no password or credential material was read or modified.
- `bash -n scripts/apply-migrations.sh` passed.
- An immediate idempotency run of `CONFIGURATION=Release DOTNET_BIN=/tmp/bodylife-dotnet/dotnet DOTNET_ROOT=/tmp/bodylife-dotnet ./scripts/apply-migrations.sh` built successfully and reported that the database was already up to date.
- Focused PostgreSQL readiness validation passed 2 tests covering fully migrated healthy state and connected-but-pending unhealthy state.
- The restarted Development app returned `200 OK` from `/health/ready` with explicit current-schema status for PostgreSQL.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 107 PostgreSQL infrastructure tests, 10 Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- No migration was generated because the failure was local migration drift and the required forward migration already existed.
- `graphify update .` completed the structural rebuild with 3019 nodes, 4938 edges and 469 communities.
- `graphify . --update` was attempted for the progress/local-development documentation changes but stopped because no semantic extraction LLM backend is configured.

Commit:

- `fix(infra): guard against pending migrations`.

Next recommended step:

- Resume the planned permission-aware `AssignOrChangeCard` Razor/htmx profile action with expected current assignment id, explicit assign/change/clear intent, replacement/clear reason, inline occupied-card/stale errors and canonical workspace reread.

## Step 41 - Reception card assignment UI workflow

Status: completed.

Plan alignment:

- Reconfirm that Milestones 1 and 2 are complete and accepted, while Milestone 3 Clients/Search remains the active roadmap milestone.
- Treat Step 40 as a corrective Milestone 1 readiness guard, not a roadmap reorder or a move into a later business module.
- Continue with the already implemented `AssignOrChangeCard` public command instead of adding card mutation to `UpdateClient` or introducing another persistence path.
- Keep CreateClient UI, Milestone 3 acceptance review, MembershipTypes and Memberships outside this step.

Scope:

- Add a permission-aware card action to the canonical client profile only when `GetClientProfile` returns the `clients.assign_or_change_card` allowed action.
- Add a dedicated card form model with client id, expected current assignment id, new card number, explicit clear intent, reason, idempotency key and preserved reception search context.
- Keep first assignment compact: require a new card number, omit clear/reason controls and rely on the command to authorize and normalize the submitted card.
- For an existing assignment, show the canonical current card, a binary clear-card checkbox, a replacement/reissue card input and a required reason shared by replace/reissue/clear semantics.
- Add a small progressive enhancement that disables the new-card field when clear intent is selected; the server command remains authoritative and the ordinary non-JavaScript POST path remains valid.
- Invoke the existing typed `AssignOrChangeCard` command with the authenticated actor/session/correlation context and reason in the command envelope; add no direct business writes or card rules to Razor/PageModel code.
- Return validation and occupied-card errors inside the card action while preserving the submitted expected assignment id, intent and idempotency key so a concurrent card change cannot be silently accepted on retry.
- On stale, concurrency, permission or not-found outcomes, reread the entire canonical workspace, discard submitted mutation fields, generate a fresh form/version/key and reopen the action with the command error.
- After assign/change/clear success, verify the command reread target, reread the complete reception workspace, show the audit reference and keep the original search query canonical: an old or cleared exact card becomes no-match while the selected profile shows current state.
- Reuse the existing htmx outerHTML workspace/form targets, request dropping, busy text, disabled submit behavior, progressive POST/redirect path and operation-message treatment.
- Add restrained tablet/phone styling for the current-card summary, destructive clear control, fields and footer without nesting cards or hiding warnings/actions.
- Seed isolated UI smoke clients for existing-card change/clear, first card assignment, stale replacement and an occupied card owned by another client.
- Add test-only PostgreSQL evidence reads for current card, assignment history rows, action-specific audit and command idempotency; add a test-only canonical replacement helper solely to create an external stale state.
- Add Playwright workflows for tablet occupied conflict/rollback, change, exact-card lookup and clear; phone first assignment without reason; stale canonical refresh and successful retry.
- Prove current/old/new exact-card search behavior, server warnings, action-specific `card.assigned`/`card.changed`/`card.cleared` audit, preserved assignment history, idempotency and observed busy/disabled submission.

Validation:

- Release solution and UI smoke project builds passed with 0 warnings/errors after the product and test wiring.
- The first focused reception run passed 8 of 10 tests; all three new card workflows passed, while the existing tablet/phone exact-profile cases found the card number twice after the new closed action panel was added. Scoping that old assertion to canonical profile metadata fixed only the strict locator.
- Final focused reception validation passed all 10 tests in 30 seconds.
- Opt-in visual capture produced tablet occupied-error, change-success and clear-success states plus phone first-assignment form/success states. Inspection confirmed readable action order, reason only for replace/clear, reachable touch controls, visible warnings/errors and no horizontal overflow at 1024x768 or 390x844.
- Focused PostgreSQL `AssignOrChangeCard` command regression validation passed all 11 tests covering accepted/denied actors, history, all audit actions, validation, stale state, replay, rollback and concurrency.
- Full Playwright smoke validation passed all 13 tests, including existing authentication, authorization, search/profile, UpdateClient and JavaScript-disabled regressions.
- The first format verification reported only four indentation spaces in one C# property pattern; the mechanical whitespace was corrected and the repeated formatter/analyzer gate passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 107 PostgreSQL infrastructure tests, 13 Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- No migration was generated because the UI uses the existing clients, assignment history, audit and idempotency schema.
- `graphify update .` completed the structural rebuild with 3051 nodes, 5083 edges and 480 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(ui): add card assignment workflow`.

Next recommended step:

- Add the permission-aware CreateClient Razor/htmx workflow from the reception no-match/search context with optional initial card, duplicate-candidate review and exact acknowledgements, busy/idempotent submission, inline card-conflict/stale-safe errors and canonical open of the newly created profile. Keep the Milestone 3 acceptance review as the following documentation/verification step.

## Step 42 - Reception client creation UI workflow

Status: completed.

Plan alignment:

- Reconfirm that Milestones 1 and 2 remain complete and that Milestone 3 Clients/Search is still the active roadmap milestone.
- Complete the remaining `CreateClient` reception workflow listed by Milestone 3 using the existing command, duplicate query, audit, idempotency and PostgreSQL constraints rather than adding another write path.
- Keep the next step limited to a Milestone 3 acceptance review; MembershipTypes, Memberships and later vertical-slice modules remain outside this step.

Scope:

- Extend successful `SearchClients` results with a server-owned `clients.create` allowed action while keeping denied and invalid results permission-empty.
- Show the create action only after a successful zero-result search with no selected or auto-opened profile and a server-granted create permission.
- Add a dedicated create form model that carries identity, optional initial card, operational status, note, idempotency key and the complete reception search context.
- Prefill the optional initial card only for explicit card-mode no-match searches so ambiguous auto/name/phone terms are never guessed into business fields.
- Resolve the authenticated actor/session/correlation context in the PageModel and invoke the existing typed `CreateClient` command without direct persistence or duplicate/card rules in Razor code.
- Preserve submitted values and the idempotency key across validation, current-card conflict, duplicate-review and retry responses.
- Query duplicate candidates only after the command reports a duplicate acknowledgement error, then render only the current server candidate set.
- Require an explicit checkbox and reason for every exact matched-client/warning-type pair while the command revalidates the complete acknowledgement set inside its PostgreSQL transaction.
- On canonical permission loss, rebuild the entire workspace so the server-owned create action disappears together with the denied search result.
- After success, verify the command reread target, reread the full reception workspace, open the canonical new profile, update the search row and show the business-audit reference.
- Preserve ordinary non-JavaScript POST/redirect behavior while using stable htmx targets, canonical `HX-Push-Url`, in-flight request dropping and visible busy/disabled submit state.
- Add tablet/phone styling for the no-match create panel, identity/card fields, duplicate warnings, inline errors and action footer without hiding the profile placeholder or causing horizontal overflow.
- Extend the isolated UI smoke PostgreSQL fixture with dedicated duplicate and occupied-card clients plus direct evidence queries for client rows, current card, normalized phone, `client.created` audit, CreateClient idempotency and duplicate acknowledgements.
- Add tablet Playwright coverage for occupied-card rollback, exact duplicate-warning rejection, exact acknowledgements, successful creation with initial card and canonical exact-card profile/search reread.
- Add phone Playwright coverage for creation without a card, the canonical no-card warning and persisted audit/idempotency evidence.

Validation:

- Release Web and UI smoke project builds passed with 0 warnings/errors after product and test wiring.
- Focused PostgreSQL `CreateClient` command plus `SearchClients` query regression validation passed all 18 tests.
- Focused PostgreSQL-backed Playwright validation passed both new CreateClient workflows in 9 seconds.
- Opt-in visual capture produced tablet duplicate-review/success and phone create-form/success screenshots. Inspection confirmed readable warning/action order, reachable touch controls, canonical result/profile state and no horizontal overflow or incoherent overlap at 1024x768 and 390x844.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore` passed without changes.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 107 PostgreSQL infrastructure tests, 15 Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- No migration was generated because this UI uses the existing clients, card assignment, duplicate acknowledgement, audit and idempotency schema.
- `graphify update .` completed the structural rebuild with 3085 nodes, 5217 edges and 483 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(ui): add client creation workflow`.

Next recommended step:

- Run the Milestone 3 acceptance review against every roadmap criterion and required test category, record objective evidence and close only discovered Clients/Search gaps before starting Milestone 4 MembershipTypes.

## Step 43 - Milestone 3 acceptance review

Status: completed.

Plan alignment:

- Keep Milestone 3 Clients/Search active until every roadmap task, acceptance criterion and required test category has direct repository evidence.
- Treat missing later Memberships state, client history, reports and audit timeline composition as dependencies of their owning roadmap milestones rather than pulling those modules into Clients/Search.
- Start no MembershipTypes implementation inside this review step.

Scope:

- Create `docs/milestone-3-acceptance-review.md` with a decision, completed-foundation matrix, all eight acceptance criteria, all six required test areas, scope/risk checks and the Milestone 4 handoff.
- Verify the clients/card/duplicate migrations, normalized identity contract, all three command handlers, search/profile queries, business-audit context, canonical rereads and reception UI against their governing docs.
- Confirm partial unique current-card indexes and raw/command-level concurrency tests prove one current card per client, one current client per card and one complete committed workflow under races.
- Confirm CreateClient, UpdateClient and AssignOrChangeCard tests cover accepted/denied actors, validation, exact duplicate acknowledgements, idempotent replay/change rejection, stale/concurrency behavior, transaction rollback, audit and reread targets.
- Confirm search tests cover exact/partial card, name, normalized phone, last four, inactive visibility, stable pagination, permission denial, validation and no-match behavior.
- Confirm Playwright covers exact-card auto-open, ambiguous selection, no-match/create, profile, update, card lifecycle, duplicate review, stale refresh, busy/disabled submission and progressive search fallback on the target viewports.
- Identify one acceptance-evidence gap: role-based Playwright interactions existed, but the documented 44x44 px touch-target minimum was not measured automatically.
- Add tablet/phone Playwright assertions for the rendered search input/button/clear link, all mode segments, inactive control, profile action summaries and every client result row; no product CSS change was required.
- Record Milestone 3 as accepted and keep fuzzy search, merge, scanner-specific identity, import cleanup, client portal/API/offline/multi-tenant scope absent.

Validation:

- Focused `ReceptionSearchAndProfileReadPathWorksOnTargetViewport` Playwright validation passed both tablet and phone cases with the new rendered 44x44 px touch-target assertions.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 34 core tests, 35 web tests, 107 PostgreSQL infrastructure tests, 15 Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- No migration or product behavior change was required; this step adds acceptance documentation and closes one automated UI quality-gate gap.
- `graphify update .` completed the structural rebuild with 3098 nodes, 5236 edges and 475 communities.
- `graphify . --update` was attempted for the acceptance/progress documentation changes but stopped because no semantic extraction LLM backend is configured.

Commits:

- `test(ui): verify reception touch targets`.
- `docs(clients): accept milestone 3`.

Next recommended step:

- Start Milestone 4 with a small MembershipTypes domain/application contract step: reconcile ADR-011 and interaction contracts, define the typed create/edit/deactivate/query shapes, and add focused validation/lifecycle tests before adding PostgreSQL schema or Owner catalog UI.

## Step 44 - MembershipTypes catalog contracts and rules

Status: completed.

Plan alignment:

- Start Milestone 4 only after the accepted Milestone 3 handoff and keep MembershipTypes as the sole owner of future-sale catalog values.
- Implement the public domain/application contract before persistence so later handlers, schema and UI share one typed validation and lifecycle vocabulary.
- Keep PostgreSQL records/mapping/migration, command handlers, audit writes, query implementation and Owner catalog UI outside this step.
- Keep issued Membership snapshot persistence and immutability tests in Milestone 5, where the issued-membership source fact exists.

Scope:

- Add `MembershipTypeCatalogValues` and `MembershipTypeCatalogRules` for required display name, normalized whitespace/comment, positive `duration_days`, non-negative `visits_limit` and canonical non-negative `Money`.
- Preserve catalog display casing and avoid inventing a duplicate/similar-name block while the roadmap still identifies that policy as an open product risk.
- Add typed `CreateMembershipTypeCommand`, `EditMembershipTypeCommand` and `DeactivateMembershipTypeCommand` records using the common operational envelope.
- Default create to active while retaining the interaction contract's explicit inactive-create option.
- Keep active-state changes out of Edit; Deactivate is a separate workflow carrying membership type id and expected `updated_at` version, with reason/comment supplied by the common envelope.
- Expose no hard-delete command or mutable issued-membership API.
- Add `MembershipTypeCatalogItem` as the future catalog/issue read shape with catalog values, active state and lifecycle timestamps.
- Add `GetMembershipTypesForIssueQuery` with ordinary active-only default and optional inactive inclusion for the future Owner catalog context.
- Add `GetMembershipTypesForIssueResult` with permission/result shape and a contract guard that rejects inactive rows from an ordinary issue result.
- Add stable Owner-only create/edit/deactivate action keys matching the existing `BodyLife.OwnerOnly` policy name.
- Add focused pure tests for normalization, control/blank names, duration, visit and price boundaries, command shapes/defaults/versioning, hard-delete absence, query defaults, inactive-result rejection, Owner catalog visibility, denied results and action keys.

Validation:

- Focused `FullyQualifiedName~MembershipType` core validation passed all 20 test cases.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore` passed without changes.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 54 core tests, 35 web tests, 107 PostgreSQL infrastructure tests, 15 Playwright smoke tests and EF migration listing through `20260710113814_AddDuplicateWarningAcknowledgements`.
- No migration was generated because this step intentionally defines only public contracts and pure rules.
- `graphify update .` completed the structural rebuild with 3154 nodes, 5342 edges and 478 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(membership-types): define catalog contracts`.

Next recommended step:

- Add only the `membership_types` EF Core/Npgsql storage slice: record/configuration, DbContext registration, reviewable migration and PostgreSQL tests for lifecycle/check/index constraints. Keep command handlers, audit workflows, query implementation and Owner UI for following steps.

## Step 45 - MembershipTypes PostgreSQL storage

Status: completed.

Plan alignment:

- Continue Milestone 4 with its first persistence task after the public catalog contract was fixed in Step 44.
- Keep the table as the canonical catalog for future sales while leaving immutable issued-membership snapshots in Milestone 5.
- Keep command handlers, authorization, idempotency, audit writes, query implementation and Owner UI outside this storage-only step.
- Do not introduce a unique membership-type name rule while duplicate/similar-name policy remains an explicit roadmap risk.

Scope:

- Add the internal `MembershipTypeRecord` with the accepted `membership_types` fields: id, name, duration, visit limit, money amount/currency, active state, comment and lifecycle timestamps.
- Add an assembly-discovered EF Core configuration under the MembershipTypes persistence boundary; the existing `BodyLifeDbContext` configuration scan required no manual registration change.
- Map price to PostgreSQL `numeric` without inventing a precision/scale restriction that is absent from the accepted Money contract.
- Add database checks for non-empty name/comment semantics, positive duration, non-negative visits/price, trimmed uppercase non-empty currency, monotonic timestamps and complete active/deactivated lifecycle state.
- Add a non-unique partial `(name, id) where is_active` index for the future ordinary issue selector while retaining inactive rows for owner/history/report reads.
- Generate and review `20260712192355_AddMembershipTypesCatalog`, including model snapshot and reversible table creation/drop operations.
- Add five disposable PostgreSQL tests proving migration metadata, the active-selector index, valid active/inactive rows, zero boundaries, duplicate-name allowance, all required value checks, lifecycle rejection and row-preserving deactivation.

Validation:

- Focused PostgreSQL `PostgreSqlMembershipTypesStorageTests` validation passed all 5 tests against Docker PostgreSQL.
- Generated idempotent migration SQL was reviewed from `/tmp/bodylife-membership-types.sql`; it contains the expected table, eight checks, partial active index and migration-history write.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift after the generated migration.
- `scripts/apply-migrations.sh` applied `20260712192355_AddMembershipTypesCatalog` to the local Docker development database through the normal forward EF workflow.
- The running Development app returned `200 OK` from `/health/ready`, with PostgreSQL reporting that the schema is current.
- The first format check found UTF-8 BOM emitted by `dotnet-ef`; the three generated files were normalized to the repository UTF-8-without-BOM convention and the repeated format/analyzer gate passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 54 core tests, 35 web tests, 112 PostgreSQL infrastructure tests, 15 Playwright smoke tests and EF migration listing through `20260712192355_AddMembershipTypesCatalog`.
- `graphify update .` completed the structural rebuild with 3190 nodes, 5413 edges and 483 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(membership-types): add catalog storage`.

Next recommended step:

- Implement only the persistence-backed `CreateMembershipType` command workflow with Owner-only authorization, catalog normalization, idempotency, one PostgreSQL transaction, append-only `membership_type.created` audit and canonical reread target. Keep edit/deactivate/query/UI as following steps.

## Step 46 - CreateMembershipType command workflow

Status: completed.

Plan alignment:

- Continue Milestone 4 after the catalog contracts and PostgreSQL storage foundation without starting the broader Owner settings UI.
- Implement only `CreateMembershipType`; keep edit, deactivate, catalog/issue queries and UI as separate following steps.
- Preserve MembershipTypes ownership of future-sale catalog values and create no issued membership or Memberships recalculation side effect.
- Keep duplicate/similar type names allowed until the roadmap's open product policy is resolved.

Scope:

- Add a scoped `IBodyLifeCommandHandler<CreateMembershipTypeCommand>` registration and persistence-backed handler.
- Reject non-Owner actor shapes before mutation and revalidate the canonical active Owner account plus unexpired matching session inside the transaction.
- Reuse `MembershipTypeCatalogRules` so the persisted name, comment, duration, visits and Money values follow the public contract rather than duplicating catalog rules in persistence.
- Validate and normalize the operational envelope, including idempotency key, correlation id, device label, entry origin and required context for non-normal origins.
- Fingerprint normalized business input and actor/session context; exact replay returns the original entity/audit ids, while changed or concurrent reuse returns stable `duplicate_submission` without duplicate rows.
- Create the catalog row, append `membership_type.created` with the complete catalog summary and store the succeeded idempotency record in one PostgreSQL `ReadCommitted` transaction.
- Use the server clock for `created_at`, `updated_at`, `deactivated_at` and audit `recorded_at`; preserve envelope `occurred_at` separately in audit.
- Support the contract's default active creation and explicit inactive creation with a complete deactivation lifecycle timestamp.
- Return `membership_type` as both primary entity and canonical reread target together with the audit entry id.
- Map idempotency uniqueness, serialization and deadlock failures to stable command errors while allowing unexpected persistence failures to surface after transaction rollback.
- Add stable MembershipType audit action constants for later edit/deactivate workflows without implementing those workflows now.
- Add eight disposable PostgreSQL command tests for successful normalization/audit/idempotency, Owner-only permissions, canonical account/session denial, validation, inactive lifecycle, replay/change rejection, concurrent same-key protection and atomic rollback when audit persistence fails.

Validation:

- Focused `PostgreSqlCreateMembershipTypeCommandTests` validation passed all 8 tests against Docker PostgreSQL.
- The concurrent changed-payload/same-key PostgreSQL test passed five repeated runs, each committing one complete catalog/audit/idempotency workflow and rejecting the competing payload.
- Focused MembershipTypes regression validation passed 20 core tests and 13 PostgreSQL tests, including the five storage tests from Step 45.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity minimal` passed after correcting two xUnit predicate-overload analyzer findings in the new concurrency test.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated because the workflow uses the existing membership type, audit and idempotency schema.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 54 core tests, 35 web tests, 120 PostgreSQL infrastructure tests, 15 Playwright smoke tests and EF migration listing through `20260712192355_AddMembershipTypesCatalog`.
- The restarted Development app loaded the new DI registration and returned `200 OK` from `/health/ready` with PostgreSQL schema current.
- `graphify update .` completed the structural rebuild with 3268 nodes, 5660 edges and 482 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(membership-types): implement create workflow`.

Next recommended step:

- Implement only the persistence-backed `EditMembershipType` command with Owner-only canonical authorization, expected `updated_at` stale-state protection, normalized future catalog fields, required reason/comment policy for meaningful changes, idempotency, before/after `membership_type.edited` audit and canonical reread. Keep deactivate/query/UI for later steps.

## Step 47 - EditMembershipType command workflow

Status: completed.

Plan alignment:

- Continue Milestone 4 with the second catalog mutation after create, without starting deactivate, catalog queries or Owner UI.
- Change only future-sale catalog values; no issued membership, issued snapshot or Memberships recalculation path is read or mutated.
- Preserve `is_active` and `deactivated_at` exactly so lifecycle changes remain owned by the separate `DeactivateMembershipType` command.
- Keep duplicate/similar names allowed until the roadmap's open product policy is resolved.

Scope:

- Add and register a scoped `IBodyLifeCommandHandler<EditMembershipTypeCommand>`.
- Reject non-Owner actor shapes before persistence and revalidate the canonical active Owner account plus unexpired matching session inside the transaction.
- Validate membership type id, expected `updated_at`, normalized catalog values, operational envelope and a required audit reason or command comment for every meaningful edit.
- Lock the target `membership_types` row with PostgreSQL `FOR UPDATE` in one `ReadCommitted` transaction before checking idempotency and stale state.
- Compare the locked row with the expected UTC version, return stable `not_found`/`stale_state`, reject normalized no-op edits and advance `updated_at` monotonically.
- Update only name, duration, visit limit, price and catalog comment while retaining created/active/deactivation lifecycle fields.
- Fingerprint normalized target/version/catalog and actor/session/envelope semantics; exact replay returns the original entity/audit ids while changed key reuse returns `duplicate_submission`.
- Check idempotency after obtaining the row lock so concurrent exact retries wait for and replay the one committed workflow instead of failing as stale.
- Append `membership_type.edited` with full before/after catalog and lifecycle summaries plus normalized reason/comment, then persist edit, audit and idempotency in the same transaction.
- Return `membership_type` as both primary entity and canonical reread target; no migration or schema change is required.
- Add nine disposable PostgreSQL tests covering canonical edit/audit, permissions, canonical session denial, validation/reason policy, missing/stale/no-op behavior, inactive lifecycle preservation, replay/change rejection, row-lock concurrency, concurrent exact replay and atomic rollback on audit failure.

Validation:

- Release Infrastructure build passed with 0 warnings and 0 errors.
- Focused `PostgreSqlEditMembershipTypeCommandTests` validation passed all 9 tests against Docker PostgreSQL.
- Focused MembershipTypes regression validation passed 20 core tests and 22 PostgreSQL tests, including create and storage coverage.
- Both concurrent edit tests passed five repeated runs after confirming the filter selected exactly 2 tests per run.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity minimal` passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build/analyzers/formatting, 54 core tests, 35 web tests, 129 PostgreSQL infrastructure tests, 15 Playwright smoke tests and EF migration listing through `20260712192355_AddMembershipTypesCatalog`.
- The restarted Development app loaded the edit-handler DI registration and returned `200 OK` from `/health/ready` with PostgreSQL schema current.
- `graphify update .` completed the structural rebuild with 3325 nodes, 5871 edges and 479 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(membership-types): implement edit workflow`.

Next recommended step:

- Implement only the persistence-backed `DeactivateMembershipType` command with Owner-only canonical authorization, expected `updated_at` stale-state protection, required reason/comment, row locking, idempotency, `is_active`/`deactivated_at` lifecycle mutation, before/after `membership_type.deactivated` audit and canonical reread. Keep query implementation and Owner UI for later steps.

## Step 48 - DeactivateMembershipType command workflow

Status: completed.

Plan alignment:

- Complete Milestone 4's third catalog mutation after create/edit without starting catalog queries or Owner UI.
- Remove a type only from future ordinary issue availability by lifecycle state; retain the canonical row, catalog values and history/report readability.
- Create no issued-membership mutation or Memberships recalculation side effect, preserving immutable issue-time snapshots.

Scope:

- Add the contract-defined stable `AlreadyInactive` command error without changing existing enum values.
- Add and register a scoped `IBodyLifeCommandHandler<DeactivateMembershipTypeCommand>`.
- Reject non-Owner actor shapes before persistence and revalidate the canonical active Owner account plus unexpired matching session inside the transaction.
- Validate membership type id, expected `updated_at`, normalized operational envelope and a required audit reason or command comment.
- Lock the target `membership_types` row with PostgreSQL `FOR UPDATE` in one `ReadCommitted` transaction before checking idempotency and lifecycle state.
- Check exact idempotency after the row lock so a concurrent retry replays the original entity/audit ids; changed key reuse returns `duplicate_submission`.
- Return `not_found` for a missing row, `stale_state` for an outdated expected version and `already_inactive` only for a new request against the current inactive state.
- Set `is_active = false`, advance `updated_at` monotonically and set `deactivated_at` to the same lifecycle timestamp while retaining every catalog field and the row itself.
- Append `membership_type.deactivated` with full before/after catalog/lifecycle summaries and normalized reason/comment, then persist lifecycle, audit and idempotency in one transaction.
- Return `membership_type` as both primary entity and canonical reread target; no migration or Memberships recalculation is required.
- Generalize the non-normal envelope validation message from creation-specific wording to all MembershipType commands.
- Add nine disposable PostgreSQL tests for successful lifecycle/audit/idempotency, Owner-only permissions, canonical session denial, validation/reason policy, missing/stale/already-inactive behavior, replay/change rejection, row-lock concurrency, concurrent exact replay, monotonic timestamps and atomic rollback on audit failure.

Validation:

- Release Infrastructure build passed with 0 warnings and 0 errors.
- Focused `PostgreSqlDeactivateMembershipTypeCommandTests` validation passed all 9 tests against Docker PostgreSQL.
- Focused MembershipTypes regression validation passed 20 core tests and 31 PostgreSQL tests across storage/create/edit/deactivate.
- Both concurrent deactivation tests passed five repeated runs after confirming the filter selected exactly 2 tests per run.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity minimal` passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 54 core tests, 35 web tests, 138 PostgreSQL infrastructure tests, 15 Playwright smoke tests and EF migration listing through `20260712192355_AddMembershipTypesCatalog`.
- The restarted Development app loaded the deactivation-handler DI registration and returned `200 OK` from `/health/ready` with PostgreSQL schema current.
- `graphify update .` completed the structural rebuild with 3381 nodes, 6076 edges and 490 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(membership-types): implement deactivation workflow`.

Next recommended step:

- Implement only the persistence-backed `GetMembershipTypesForIssue` query: active types for ordinary issue flow, inactive inclusion only for canonical Owner catalog context, deterministic ordering, allowed-action metadata and PostgreSQL query tests. Keep Owner UI for the following step.

## Step 49 - GetMembershipTypesForIssue query

Status: completed.

Plan alignment:

- Continue Milestone 4 after all three catalog mutations with the canonical read path required by future issue flow and Owner catalog UI.
- Keep ordinary issue reads active-only while retaining inactive rows only for an explicit Owner catalog context.
- Implement no UI, issued-membership snapshot, Memberships recalculation or business mutation in this query-only step.

Scope:

- Add and register a scoped persistence-backed `IBodyLifeQueryHandler<GetMembershipTypesForIssueQuery, GetMembershipTypesForIssueResult>`.
- Add MembershipTypes-local query authorization that accepts only canonical active Owner, named Admin or shared Reception/Admin accounts with matching unexpired, unended sessions.
- Deny `IncludeInactive` for Admin/shared Reception rather than silently returning a privileged catalog shape; denied results contain no rows or actions.
- Query `membership_types` with `AsNoTracking` and map every canonical catalog, Money, active-state and lifecycle field into `MembershipTypeCatalogItem`.
- For ordinary issue context, filter `is_active` before projection and order by `name, id`, matching the existing partial active-issue index.
- For Owner catalog context, include active and inactive rows with deterministic active-first, then `name, id` ordering.
- Return explicit allowed create/edit/deactivate metadata for Owner and explicit Owner-policy denials for Admin/shared Reception ordinary issue reads.
- Keep queries side-effect free: no business audit, idempotency record or tracked catalog state is created.
- Add seven disposable PostgreSQL tests covering all operational roles, active-only filtering, duplicate-name/id ordering, full field mapping, Owner inactive visibility/lifecycle, Admin privilege denial, forged/inactive/expired/ended/unknown actor denial, empty catalog and canonical reread after lifecycle change.

Validation:

- Release Infrastructure build passed with 0 warnings and 0 errors.
- The first focused PostgreSQL run exposed two real issues: EF could not translate filtering/ordering after constructing a private projection record, and the actor-denial fixture violated the database's single-Owner constraint.
- Filtering/ordering was moved to `MembershipTypeRecord` before projection; session-denial coverage now uses one Owner account with multiple sessions, while inactive-owner denial uses a separate disposable database.
- Repeated focused `PostgreSqlGetMembershipTypesForIssueQueryTests` validation then passed all 7 tests against Docker PostgreSQL.
- Focused MembershipTypes regression validation passed 20 core tests and 38 PostgreSQL tests across contracts/storage/create/edit/deactivate/query.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity minimal` passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 54 core tests, 35 web tests, 145 PostgreSQL infrastructure tests, 15 Playwright smoke tests and EF migration listing through `20260712192355_AddMembershipTypesCatalog`.
- The restarted Development app loaded the query-handler DI registration and returned `200 OK` from `/health/ready` with PostgreSQL schema current.
- `graphify update .` completed the structural rebuild with 3430 nodes, 6239 edges and 495 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(membership-types): implement catalog query`.

Next recommended step:

- Add only the Owner-only read surface for the MembershipType catalog/settings page, backed by `GetMembershipTypesForIssue(IncludeInactive: true)`, with active/inactive status, lifecycle/catalog fields, permission-safe action affordances and tablet/phone rendering tests. Keep create/edit/deactivate form submissions as following UI steps.

## Step 50 - Owner MembershipType catalog read surface

Status: completed.

Plan alignment:

- Continue Milestone 4 with only the Owner catalog read surface after the catalog query, without combining create, edit or deactivate form submissions into this step.
- Render the complete canonical catalog/lifecycle state returned by `GetMembershipTypesForIssue(IncludeInactive: true)`; the Razor page contains no duplicated business rules or direct persistence access.
- Keep the screen a compact operational Owner settings view rather than expanding it into a broad settings area or generic table-first CRUD.

Scope:

- Add an Owner-authorized `/Owner/MembershipTypes` Razor Page backed by the registered `GetMembershipTypesForIssue` handler and the authenticated request actor.
- Return `Forbid` if the persistence-backed query denies the canonical actor/session even after route authorization.
- Render active-first catalog rows with name, duration, visit limit, price/currency, comment, created/updated timestamps and inactive deactivation timestamp.
- Consume query action metadata to show the restrained `Owner managed` state only when create, edit and deactivate permissions are all allowed; no mutation control is rendered before its server workflow is wired.
- Add active/inactive counts, explicit status labels and a stable empty state without hiding inactive historical catalog rows.
- Add the Owner-only shell navigation link while keeping it absent for named/shared Admin sessions.
- Add responsive catalog row styling for tablet and phone, including stable status badges, single-column phone metadata and long-name wrapping.
- Seed one active and one inactive MembershipType in each disposable UI smoke database.
- Add three PostgreSQL-backed Playwright tests for Owner rendering at `1024x768` and `390x844`, active-first/full-field mapping, read-only form absence, no horizontal overflow and Named Admin navigation/direct-route denial.
- Serialize only the MembershipType and StaffAccounts Owner UI test classes in one xUnit collection so their independent app/database fixtures do not compete during the full suite.
- No schema or migration change is required.

Validation:

- Release Web and UI smoke project builds passed with 0 warnings and 0 errors.
- Focused `MembershipTypeCatalogSmokeTests` passed all 3 PostgreSQL-backed Playwright tests after final catalog-value assertions.
- The first full UI suite run exposed two simultaneous 30-second login timeouts in the new catalog class and existing StaffAccounts class while their independent app/database fixtures started in parallel; the remaining 16 tests passed.
- Grouping only those two Owner UI classes into `Owner UI smoke` removed the fixture contention; the full smoke suite then passed all 18 tests.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity minimal` passed without changes.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 54 core tests, 35 web tests, 145 PostgreSQL infrastructure tests, 18 Playwright smoke tests and EF migration listing through `20260712192355_AddMembershipTypesCatalog`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated.
- Full-page Playwright screenshots were inspected at both target viewports: navigation, counts, badges, rows and lifecycle metadata remained readable without overlap or horizontal scrolling; temporary screenshots stayed outside the repository.
- The restarted Development app loaded the new Razor page and returned `200 OK` from `/health/ready` with PostgreSQL schema current.
- `graphify update .` completed the structural rebuild with 3466 nodes, 6305 edges and 493 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(membership-types): add owner catalog page`.

Next recommended step:

- Add only the Owner `CreateMembershipType` form submission to the catalog page, using the existing command handler, a fresh idempotency key, busy/duplicate-submit protection, server validation/error rendering, Post/Redirect/Get canonical reread and tablet/phone Playwright coverage. Keep edit and deactivate UI for separate following steps.

## Step 51 - Owner CreateMembershipType form

Status: completed.

Plan alignment:

- Continue Milestone 4 with only the Owner create interaction after the read-only catalog surface, using the already implemented `CreateMembershipType` command and canonical catalog query.
- Keep validation, authorization, normalization, idempotency, transaction, audit and persistence ownership in the server-side command; the Razor Page only adapts representable form input and renders command outcomes.
- Keep edit and deactivate controls out of this step so each remaining catalog mutation receives its own stale-state, reason and lifecycle UI evidence.
- Create future-sale catalog data only; no issued membership, immutable snapshot or Memberships recalculation path is involved.

Scope:

- Add a dedicated create-form view model with blank initial values, default `UAH` currency, a fresh idempotency key and preservation of submitted input/errors after a rejected command.
- Add the Owner-only `OnPostCreate` Razor Page handler and invoke the existing `CreateMembershipTypeCommand` through the authenticated request-context envelope.
- Adapt missing or unrepresentable numeric/Money input into stable command-shaped validation errors without duplicating catalog business rules.
- Return `Forbid` for command permission denial and verify a successful command supplies matching primary/canonical reread ids.
- Use Post/Redirect/Get after success, issue a new form idempotency key on the canonical GET and show the returned business-audit reference.
- Reread the canonical catalog query after rejected submissions while retaining the posted form values and idempotency key for correction.
- Render a compact, unframed create section only when the query allows the create action, with antiforgery, required accessible fields and the shared busy/disabled duplicate-submit behavior.
- Map duplicate/concurrency outcomes to safe operational guidance and remove runtime parameter suffixes from otherwise user-facing validation text.
- Add responsive four-, two- and one-column layouts for desktop/tablet/phone without introducing edit or deactivate affordances.
- Add PostgreSQL smoke-evidence helpers and two Playwright cases proving busy state, server rejection without side effects, corrected resubmission with the same key, normalization, one catalog row, one audit/idempotency result, PRG canonical reread, fresh key and no horizontal overflow.
- Update the existing catalog smoke assertions to expect exactly the create form while continuing to prove edit/deactivate controls are absent.
- No schema or migration change is required.

Validation:

- Release Web and UI smoke project builds passed with 0 warnings and 0 errors.
- The first focused creation run exposed a test-only unstable role locator after the busy script changed button text; the test now uses the stable submit-button selector for that immediate state assertion.
- The next focused run exposed the runtime parameter-name suffix in a command validation message; the Razor error mapper now removes that implementation detail before display.
- Focused `MembershipTypeCreationSmokeTests` then passed both PostgreSQL-backed tablet (`1024x768`) and phone (`390x844`) workflows.
- Focused MembershipType UI regression validation passed all 5 catalog/create tests, and the full Playwright suite passed all 20 tests.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity minimal` passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 54 core tests, 35 web tests, 145 PostgreSQL infrastructure tests, 20 Playwright smoke tests and EF migration listing through `20260712192355_AddMembershipTypesCatalog`.
- Full-page Playwright screenshots were inspected at both target viewports: the form, validation/success state and catalog remained readable without overlap or horizontal scrolling; temporary screenshots stayed outside the repository.
- The restarted Development app returned `200 OK` from `/health/ready` with PostgreSQL schema current.
- `graphify update .` completed the structural rebuild with 3505 nodes, 6406 edges and 489 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(membership-types): add owner create form`.

Next recommended step:

- Add only the Owner `EditMembershipType` interaction to each catalog row, using the existing command handler, canonical row values plus expected `updated_at`, a required reason/comment, a fresh idempotency key, busy/duplicate-submit protection, stale/concurrency guidance, Post/Redirect/Get canonical reread and tablet/phone Playwright coverage. Keep deactivate UI for the following step.

## Step 52 - Owner EditMembershipType interaction

Status: completed.

Plan alignment:

- Continue Milestone 4 with only the Owner edit interaction after create, using the already implemented `EditMembershipType` command and canonical Owner catalog query.
- Keep Owner authorization, catalog normalization, no-op/stale checks, row locking, idempotency, transaction and before/after audit in the command handler; the Razor Page only adapts representable form values and renders command outcomes.
- Edit only future-sale catalog fields and preserve active/deactivated lifecycle state; no issued membership, immutable issue-time snapshot or Memberships recalculation path is read or mutated.
- Keep deactivation controls out of this step so lifecycle mutation receives a separate confirmation, stale-state and audit UI proof.

Scope:

- Add a per-row edit-form view model initialized from canonical query values with membership type id, expected `updated_at`, catalog fields and a fresh idempotency key.
- Preserve posted values, expected version and idempotency key after ordinary validation/no-op rejection so the Owner can correct the same uncommitted request.
- Add the Owner-only `OnPostEdit` Razor Page handler and invoke the existing `EditMembershipTypeCommand` with the authenticated request envelope and required audit reason.
- Adapt missing or unrepresentable numeric/Money input into stable command-shaped validation errors without duplicating catalog business rules.
- Verify successful command entity/reread ids, show the returned business-audit reference and use Post/Redirect/Get to reread the canonical catalog with fresh edit keys.
- Return `Forbid` for command permission denial.
- Treat stale state, concurrency, not-found and changed duplicate-key outcomes as canonical-refresh conditions: discard attempted values, load current catalog values plus a fresh key and require the Owner to review before resubmitting.
- Render an accessible, unframed expandable edit surface inside every canonical active or inactive catalog row only when the query allows edit.
- Include expected version/idempotency hidden values, required reason, catalog fields, antiforgery and the shared busy/disabled duplicate-submit behavior; render no deactivate control.
- Add responsive four-, two- and one-column edit layouts for desktop/tablet/phone while preserving compact catalog scanning when forms are closed.
- Add test-only PostgreSQL evidence helpers for canonical row reads, controlled stale-version advancement, edit audit/idempotency counts and persisted audit reason.
- Add two Playwright cases covering active tablet and inactive phone rows, busy state, no-op rejection without side effects, stale canonical refresh, normalized successful edit, one audit/idempotency result, required reason, lifecycle preservation, PRG fresh key and no horizontal overflow.
- Update catalog smoke expectations to include the two edit panels while continuing to prove deactivate controls are absent and Admin access remains denied.
- No schema or migration change is required.

Validation:

- Release UI smoke project build passed with 0 warnings and 0 errors.
- The first focused invocation stopped before app startup because the standalone command omitted `BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING`; rerunning with the Docker PostgreSQL connection reached the workflow normally.
- The first browser execution then exposed a test-only busy probe blocked by the required empty reason field; filling a valid probe reason before the prevented submit made the assertion exercise the shared busy handler correctly.
- Focused `MembershipTypeEditingSmokeTests` passed both PostgreSQL-backed tablet (`1024x768`) and phone (`390x844`) workflows.
- Focused MembershipType catalog/create/edit regression validation passed all 7 tests, and the full Playwright suite passed all 22 tests.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity minimal` passed without changes.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 54 core tests, 35 web tests, 145 PostgreSQL infrastructure tests, 22 Playwright smoke tests and EF migration listing through `20260712192355_AddMembershipTypesCatalog`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated.
- Full-page Playwright screenshots were inspected at both target viewports with an edited row expanded: fields, lifecycle status, reason and actions remained readable without overlap or horizontal scrolling; temporary screenshots stayed outside the repository.
- The restarted Development app loaded the new PageModel dependency and returned `200 OK` from `/health/ready` with PostgreSQL schema current.
- `graphify update .` completed the structural rebuild with 3554 nodes, 6547 edges and 482 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(membership-types): add owner edit forms`.

Next recommended step:

- Add only the Owner `DeactivateMembershipType` interaction for active catalog rows, using the existing command handler, canonical expected `updated_at`, required reason, fresh idempotency key, explicit confirmation, busy/duplicate-submit protection, stale/already-inactive canonical refresh, Post/Redirect/Get canonical reread and tablet/phone Playwright coverage. Do not add hard delete or start Milestone 5 in that step.

## Step 53 - Owner DeactivateMembershipType interaction

Status: completed.

Plan alignment:

- Complete Milestone 4's Owner catalog mutation UI with only the deactivation interaction, reusing the existing `DeactivateMembershipType` command and canonical Owner catalog query.
- Keep Owner authorization, reason/idempotency validation, row locking, stale/lifecycle checks, transaction and before/after audit in the command handler; the Razor Page only submits command input and renders canonical outcomes.
- Remove a type only from future ordinary issue availability by setting its existing lifecycle state; retain the row, catalog fields, history/report visibility and edit surface.
- Create no hard-delete path, issued-membership mutation, immutable snapshot change or Memberships recalculation side effect.

Scope:

- Add a deactivation-form view model that can be created only from a canonical active catalog row and carries membership type id, expected `updated_at`, required reason and a fresh idempotency key.
- Add the Owner-only `OnPostDeactivate` Razor Page handler and invoke the existing `DeactivateMembershipTypeCommand` with the authenticated request envelope.
- Verify successful command entity/reread ids, show the returned business-audit reference and use Post/Redirect/Get to reread the canonical catalog.
- Return `Forbid` for command permission denial.
- Preserve posted input/key for ordinary validation rejection; for stale state, concurrency, not-found, changed duplicate-key or already-inactive outcomes, reread canonical state and require review before another action.
- Keep deactivation forms only for active rows; when a concurrent canonical refresh finds the row inactive, remove the form and show page-level lifecycle guidance.
- Refactor edit/deactivate error rendering into narrow typed render-state records so each mutation can rebuild its own canonical form without mixing errors.
- Render an accessible expandable destructive panel below edit controls with required reason, antiforgery, explicit browser confirmation and the shared busy/disabled duplicate-submit behavior.
- Add responsive two-column tablet/desktop and one-column phone deactivation layout with distinct destructive semantics and no nested card surface.
- Add test-only active MembershipType seeding, controlled concurrent lifecycle transition, canonical row reads, deactivation audit/idempotency counts and persisted audit-reason evidence.
- Add two PostgreSQL-backed Playwright cases covering tablet and phone busy/confirmation state, server reason validation, stale refresh, successful deactivation, catalog-value preservation, one lifecycle audit/idempotency result, PRG key refresh, active-only form removal, already-inactive canonical refresh without a second audit/idempotency record and no horizontal overflow.
- Update existing catalog/edit smoke expectations for one hidden active-row deactivation control while retaining Admin route denial.
- No schema or migration change is required.

Validation:

- Release UI smoke project build passed with 0 warnings and 0 errors.
- Focused `MembershipTypeDeactivationSmokeTests` passed both PostgreSQL-backed tablet (`1024x768`) and phone (`390x844`) workflows.
- The first combined MembershipType regression run exposed four test-only assertions that used an accessibility-role locator for buttons hidden inside closed `<details>` elements; structural selectors now prove the hidden forms/buttons exist without treating them as currently visible controls.
- Focused MembershipType catalog/create/edit/deactivate regression validation then passed all 9 tests, and the full Playwright suite passed all 24 tests.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity minimal` passed without changes.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 54 core tests, 35 web tests, 145 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260712192355_AddMembershipTypesCatalog`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated.
- Full-page Playwright screenshots were inspected in stale-warning state at both target viewports: destructive panel, warning, reason and action remained readable without overlap or horizontal scrolling; temporary screenshots stayed outside the repository.
- The restarted Development app loaded the new PageModel dependency and returned `200 OK` from `/health/ready` with PostgreSQL schema current.
- `graphify update .` completed the structural rebuild with 3604 nodes, 6671 edges and 497 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(membership-types): add owner deactivation forms`.

Next recommended step:

- Run a small Milestone 4 acceptance checkpoint against the roadmap and record any intentionally deferred criterion, especially the issued-membership snapshot contract that depends on Milestone 5. If the checkpoint is clean, the following implementation step should start only Milestone 5's domain contract for immutable issue-time snapshots and inclusive base-end-date behavior, without persistence or UI yet.

## Step 54 - Milestone 4 acceptance checkpoint

Status: completed.

Plan alignment:

- Review every Milestone 4 roadmap task, acceptance criterion and required test category before starting Memberships work.
- Accept only behavior owned by the MembershipTypes boundary and do not pull issued-membership persistence, calculations, reports or history UI forward from their owning milestones.
- Treat the issued-membership snapshot-after-catalog-edit proof as an explicit cross-milestone gate because the roadmap creates that source fact in Milestone 5 and asks Milestone 4 to add the contract test once it exists.

Scope:

- Add `docs/milestone-4-acceptance-review.md` with the checkpoint decision, completed-foundation matrix, all six acceptance criteria, all five required test areas, scope/risk checks and the constrained Milestone 5 handoff.
- Confirm the catalog migration, domain rules, create/edit/deactivate contracts and handlers, active-only issue query, retained inactive rows, Owner UI and business-audit paths against ADR-011 and the architecture/interaction/data contracts.
- Confirm command suites prove Owner-only authorization, validation, idempotency, stale/concurrency behavior, transaction rollback, audit and canonical reread targets against PostgreSQL.
- Confirm query tests prove active-only ordinary output for every operational role, Owner-only inactive inclusion and query-only behavior.
- Confirm Playwright covers Owner catalog/read/create/edit/deactivate behavior on tablet and phone plus named Admin route/navigation denial.
- Record the Milestone 4 catalog slice as accepted for Milestone 5 handoff while leaving final snapshot immutability acceptance open until the first issued-membership persistence slice can prove it.
- Add no product code, schema, migration or UI change in this checkpoint.

Validation:

- The acceptance review found no missing behavior or test category owned solely by MembershipTypes; issued snapshot immutability remains the one explicit dependency gate for Milestone 5.
- Focused `FullyQualifiedName~MembershipType` core validation passed all 20 catalog contract/rule tests.
- Focused `FullyQualifiedName~MembershipType` PostgreSQL validation passed all 38 storage/create/edit/deactivate/query tests against Docker PostgreSQL.
- Focused `FullyQualifiedName~MembershipType` Playwright validation passed all 9 catalog/create/edit/deactivate tests across their configured tablet/phone cases, including named Admin denial.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 54 core tests, 35 web tests, 145 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260712192355_AddMembershipTypesCatalog`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated.
- The running Development app returned `200 OK` from `/health/ready` against the current PostgreSQL schema.
- `graphify . --update` was attempted for the acceptance/progress documentation changes but stopped because no semantic extraction LLM backend is configured; no tracked graph artifact changed.

Commit:

- `docs(membership-types): record milestone 4 acceptance`.

Next recommended step:

- Start only Milestone 5's domain/application contract for immutable issue-time MembershipType snapshots and inclusive `base_end_date = start_date + duration_days - 1 day`, with focused domain tests and no persistence, recalculation cache or UI yet.

## Step 55 - Membership issue snapshot and inclusive-date contracts

Status: completed.

Plan alignment:

- Start Milestone 5 only after the Step 54 handoff and keep this first slice inside the Memberships domain/application boundary.
- Lock the accepted inclusive formula from the roadmap, data architecture and vertical slice: `base_end_date = start_date + duration_days - 1 day`; this supersedes the older unresolved wording in the first-version requirements.
- Copy future issued-membership values from the public MembershipTypes catalog contract into a separate immutable value rather than retaining a live mutable catalog reference.
- Keep effective end date, active state, remaining/negative visits, recalculation, source-fact persistence, opening state, cache tables, commands and UI outside this step.
- Avoid fixing the still-open multiple-active-membership, visit-allocation and negative-closure product decisions while they are irrelevant to snapshot/date arithmetic.

Scope:

- Add `IssuedMembershipSnapshot` with get-only type name, duration days, visits limit and Money values; construction reuses MembershipTypes-owned canonical catalog validation.
- Add `MembershipDateRules.CalculateBaseEndDate` with positive-duration validation, inclusive day-number arithmetic and an explicit supported-calendar range guard.
- Add `MembershipIssueTerms.FromActiveMembershipType` as the narrow cross-module issue boundary: require a non-empty MembershipType identity, reject inactive ordinary-issue types, copy a standalone snapshot and calculate the base end date from the copied duration.
- Expose only the MembershipType reference id, immutable snapshot, start date and derived base end date; expose no `MembershipTypeCatalogItem`, editable setters or `EffectiveEndDate` field.
- Add focused domain tests for the vertical-slice `2026-07-01 + 30 days = 2026-07-30` case, one-day duration, year/leap-day boundaries, invalid/overflowing duration, canonical snapshot validation, active/inactive catalog eligibility, required identity, copied values after a later catalog replacement and the absence of mutable/end-date contracts.
- Add no `IssueMembership` command/handler, persistence record, migration, recalculation/cache behavior or UI.

Validation:

- Focused `FullyQualifiedName~Memberships` core validation passed all 13 new snapshot/date contract cases.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity minimal` passed without changes.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 67 core tests, 35 web tests, 145 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260712192355_AddMembershipTypesCatalog`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated.
- The running Development app returned `200 OK` from `/health/ready`; this domain-only step requires no app restart.
- `graphify update .` completed the structural rebuild with 3645 nodes, 6733 edges and 495 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): add issue snapshot contracts`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the `issued_memberships` canonical source-fact PostgreSQL schema, EF mapping, migration and constraint/storage tests using these immutable snapshot and inclusive base-end-date contracts. Keep `IssueMembership` handling, recalculation/cache tables, opening states and UI for later steps.

## Step 56 - Issued membership PostgreSQL source-fact storage

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the canonical `issued_memberships` source fact after the snapshot/date contracts from Step 55.
- Store copied issue-time values separately from the mutable MembershipType catalog and keep `effective_end_date`, visits, negative state, extensions and warnings out of this source table.
- Preserve the roadmap's still-open multiple-active-memberships decision by adding no unique/current-membership constraint or index in this step.
- Keep `entry_batch_id` as a nullable source metadata UUID without a premature foreign key; the `entry_batches` source table belongs to the later backfill/paper-fallback slice.
- Add no `IssueMembership` command/handler, audit workflow, opening state, adjustment, recalculation/cache table, query or UI.

Scope:

- Add the internal `IssuedMembershipRecord` with client and MembershipType references, immutable snapshot columns, start/base-end dates, issue actor/time, lifecycle status, entry origin/batch and optional comment.
- Add an assembly-discovered EF Core configuration under the Memberships persistence boundary; no manual `BodyLifeDbContext` registration change is required.
- Map `DateOnly` start/base-end values to PostgreSQL `date` and enforce `base_end_date = start_date + duration_days_snapshot - 1` with a database check.
- Add checks for non-empty snapshot name, positive snapshot duration, non-negative visit limit and price, canonical uppercase currency, non-empty optional comment, accepted lifecycle status and accepted entry origin.
- Keep expiration as derived Memberships state rather than a source lifecycle status; the source status accepts only `active`, `canceled` and `corrected`.
- Add `Restrict` foreign keys to `clients`, `membership_types` and issuing `accounts` so historical source facts cannot be removed through principal deletion.
- Add a non-unique client timeline index on `(client_id, start_date desc, issued_at desc)` plus supporting MembershipType and issuer indexes.
- Generate and review reversible migration `20260713091512_AddIssuedMemberships`, including the updated model snapshot and idempotent SQL kept outside the repository.
- Add six PostgreSQL-backed tests covering migration shape, complete source metadata, snapshot/date/text/lifecycle/origin constraints, all three foreign keys, retained snapshot values after catalog edit and blocked deletion of a referenced MembershipType.

Validation:

- The first infrastructure-test build found two test-only compile issues: one out-of-position named argument and a missing local fixed-time helper; both were corrected before any test run.
- Release infrastructure-test project build then passed with 0 warnings and 0 errors.
- Focused `PostgreSqlIssuedMembershipsStorageTests` validation passed all 6 tests against Docker PostgreSQL.
- Idempotent SQL from `20260712192355_AddMembershipTypesCatalog` through `20260713091512_AddIssuedMemberships` was generated to `/tmp/bodylife-add-issued-memberships.sql` and reviewed for the expected table, checks, `Restrict` foreign keys, indexes and migration-history transaction.
- The first formatting verification found only a composite-index indentation issue and the EF-generated migration BOM; targeted whitespace formatting fixed both, and final `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity minimal` passed.
- `ASPNETCORE_ENVIRONMENT=Development CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet ./scripts/apply-migrations.sh` applied `20260713091512_AddIssuedMemberships` to the local Docker development database through the normal forward EF workflow.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 67 core tests, 35 web tests, 151 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713091512_AddIssuedMemberships`.
- The restarted Development app loaded the new EF model/migration assembly and returned `200 OK` from `/health/ready` against the migrated PostgreSQL schema.
- `graphify update .` completed the structural rebuild with 3695 nodes, 6861 edges and 511 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): add issued membership storage`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the initial Memberships calculated-state domain contract for a newly issued snapshot: zero counted visits/extensions, signed remaining visits from the snapshot, zero negative balance, base effective end date and inclusive active-by-date behavior. Keep persistence cache, `IssueMembership` handling, later visit/freeze inputs and UI outside that step.

## Step 57 - Initial membership calculated-state contract

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the initial Memberships-owned derived state for the issued snapshot established in Steps 55-56.
- Keep `remaining_visits`, negative balance, first-negative metadata, extension days and effective end date under the Memberships boundary; expose no duplicated formulas to UI, reports or other modules.
- Treat date activity as an inclusive query-time calculation (`as_of_date <= effective_end_date`) that is distinct from visit balance and lifecycle cancellation/correction status.
- Do not persist an `is_active` flag or make `effective_end_date` directly mutable.
- Keep lifecycle composition, PostgreSQL cache persistence, `IssueMembership` handling, visits, freezes, non-working days, adjustments, opening states, queries and UI outside this step.

Scope:

- Add immutable `MembershipCalculatedState` output with counted visits, signed remaining visits, negative balance, first-negative visit metadata, extension days, effective end date and last-counted-visit time.
- Add `MembershipStateCalculator.CalculateInitial` as the narrow initial-state calculation from `MembershipIssueTerms`.
- Initialize a new issue with zero counted visits, the snapshot visit limit as remaining visits, zero negative balance, no first-negative metadata, zero extension days, the inclusive base end date as effective end date and no last counted visit.
- Add `MembershipDateRules.IsActiveByDate`; the effective end date is included and the following day is inactive by date.
- Keep calculated-state construction internal and every exposed property get-only so callers cannot manufacture or edit derived truth.
- Add five focused tests for canonical initialization, a zero-visit snapshot, inclusive date activity, null input and the immutable/public API boundary.

Validation:

- Focused `FullyQualifiedName~Memberships` core validation passed all 18 tests, including the 5 new initial-state cases.
- `/tmp/bodylife-dotnet/dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore --verbosity minimal` passed without changes.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 72 core tests, 35 web tests, 151 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713091512_AddIssuedMemberships`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated.
- The previous Development process was no longer listening on the first readiness probe; the app was restarted from the validated Release build and `/health/ready` returned `200 OK` against Docker PostgreSQL.
- `graphify update .` completed the structural rebuild with 3716 nodes, 6897 edges and 508 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): add initial calculated state`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the rebuildable `membership_state_cache` PostgreSQL storage, EF mapping, migration and constraint/storage tests for this calculated-state contract. Keep `IssueMembership` handling, visit/freeze/non-working-day inputs, general recalculation and UI outside that step.

## Step 58 - Membership state cache PostgreSQL storage

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the rebuildable PostgreSQL storage for the initial calculated-state contract from Step 57.
- Keep `membership_state_cache` as a dependent derived table with exactly one row per canonical `issued_memberships` source fact; deleting/replacing a cache row does not alter the source membership.
- Persist only stable derived values. Date-dependent `is_active`/`active_status`, `days_left` and warning flags remain query-time Memberships behavior and are intentionally absent from the table.
- Preserve signed `remaining_visits` and enforce `negative_balance = max(0, -remaining_visits)` as a database consistency guard without moving the formula outside Memberships ownership.
- Keep `first_negative_visit_id` as a nullable UUID without a premature foreign key because the Visits source schema does not exist yet.
- Add no cache writer/rebuild application service, `IssueMembership` handling, opening state, adjustments, extension explanation rows, visits/freezes/non-working inputs, public query or UI.

Scope:

- Add internal `MembershipStateCacheRecord` and assembly-discovered EF configuration under the Memberships persistence boundary.
- Use `membership_id` as both primary key and cascading foreign key to `issued_memberships.id`, enforcing one cache row per known membership while keeping the cache disposable.
- Store counted visits, signed remaining visits, negative balance, optional first-negative visit metadata, extension days, effective end date, optional last-counted-visit time, recalculation time and positive recalculation version.
- Add PostgreSQL checks for non-negative counted visits and extension days, formula-consistent negative balance and positive recalculation version.
- Add the planned report/read indexes for effective end date, remaining visits, open negative balance and last counted visit; the negative index is partial on `negative_balance > 0`.
- Generate reversible migration `20260713100046_AddMembershipStateCache` and update the EF model snapshot.
- Add five PostgreSQL-backed tests for migration shape and forbidden date-dependent columns, initial state storage, signed negative state metadata, constraint enforcement, FK/one-row uniqueness and delete/reinsert rebuild behavior without source-fact loss.

Validation:

- Release infrastructure-test project build passed with 0 warnings and 0 errors before migration generation.
- Focused `PostgreSqlMembershipStateCacheStorageTests` validation passed all 5 tests against Docker PostgreSQL.
- Idempotent migration SQL was generated to `/tmp/bodylife-add-membership-state-cache.sql` and reviewed for the expected table, checks, cascading source FK, partial/report indexes and migration-history transaction.
- The first formatting verification found only the EF-generated migration BOM; targeted `dotnet format whitespace` corrected the encoding, and the final solution formatting verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift.
- `scripts/apply-migrations.sh` applied `20260713100046_AddMembershipStateCache` to the local Docker development database through the normal forward EF workflow.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 72 core tests, 35 web tests, 156 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713100046_AddMembershipStateCache`.
- The Development app was restarted from the validated Release build and `/health/ready` returned `200 OK` against the migrated Docker PostgreSQL schema.
- `graphify update .` completed the structural rebuild with 3764 nodes, 7011 edges and 508 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): add state cache storage`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only a Memberships-owned initial cache rebuild/write service that rehydrates issued snapshot source facts, recalculates their initial state and upserts/compares `membership_state_cache` inside an explicit transaction. Keep `IssueMembership`, opening states, adjustments, visit/freeze/non-working inputs, public queries and UI outside that step.

## Step 59 - Initial membership state cache rebuild service

Status: completed.

Plan alignment:

- Continue Milestone 5 with only a Memberships-owned rebuild path for the source facts currently implemented: immutable `issued_memberships` snapshots.
- Rehydrate through the Memberships domain contract and recalculate with `MembershipStateCalculator`; infrastructure maps values but does not duplicate formulas.
- Validate persisted `base_end_date` against the snapshot duration and inclusive date rule instead of trusting an editable end-date input.
- Serialize rebuilds for the same membership by locking the issued source row with PostgreSQL `FOR UPDATE`; open an explicit `ReadCommitted` transaction when standalone and join, without committing, an existing command-owned transaction.
- Treat `recalculation_version = 1` as the current deterministic calculation-contract version; compare it with every stable derived field while excluding `recalculated_at` metadata from drift detection.
- Create no business audit entry because rebuilding derived cache changes no canonical business fact.
- Keep opening states, adjustments, extension explanation rows, visits/freezes/non-working inputs, `IssueMembership`, public queries and UI outside this initial-only service.

Scope:

- Add `MembershipIssueTerms.FromIssuedSnapshot` for immutable source rehydration with required MembershipType identity and canonical base-end-date verification.
- Add `MembershipStateCacheRebuilder.RebuildInitialAsync` with typed `MissingSource`, `Created`, `Repaired` and `Verified` outcomes.
- Create a missing cache row, repair drift across every stable field, verify matching state and refresh `recalculated_at` metadata using the injected `TimeProvider`.
- Let an existing EF transaction retain commit ownership so a future source-fact command can roll back the cache write atomically.
- Compare and persist counted visits, signed remaining visits, negative balance, first-negative metadata, extension days, effective end date, last-counted-visit time and the calculation version.
- Register the scoped rebuilder in `AddBodyLifePersistence`; no schema or migration change is required.
- Add two domain tests for issued-snapshot rehydration and mismatched base-end rejection.
- Add seven PostgreSQL-backed tests for missing source, immutable snapshot use after catalog edit, full drift repair, verified metadata refresh, version drift, concurrent rebuild serialization with one cache row and outer-transaction rollback ownership.

Validation:

- Focused `FullyQualifiedName~Memberships` core validation passed all 20 tests, including the 2 new source-rehydration cases.
- Release infrastructure-test project build passed with 0 warnings and 0 errors after adding the service and DI registration.
- Focused `PostgreSqlMembershipStateCacheRebuildTests` validation passed all 7 tests against Docker PostgreSQL, including concurrent create/verify serialization and command-owned transaction rollback.
- Solution formatting verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 74 core tests, 35 web tests, 163 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713100046_AddMembershipStateCache`.
- The Development app was restarted from the validated Release build and `/health/ready` returned `200 OK` against Docker PostgreSQL.
- `graphify update .` completed the final structural rebuild with 3837 nodes, 7145 edges and 527 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): add initial cache rebuild`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the canonical `membership_opening_states` PostgreSQL source-fact schema, active-row partial uniqueness, metadata/check constraints, migration and storage tests. Keep backfill commands, recalculation integration, adjustments and UI outside that step.

## Step 60 - Membership opening-state PostgreSQL storage

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the canonical PostgreSQL source-fact storage for honest membership opening states when active historical data is incomplete.
- Preserve the accepted backfill boundary: store declared state and its source/accountability metadata instead of generating synthetic visits or directly patching `membership_state_cache`.
- Keep signed declared remaining visits and enforce a formula-consistent declared negative balance without moving broader recalculation formulas into persistence.
- Allow known effective-end and extension values to remain nullable when the source cannot honestly establish them; when supplied, constrain extension days to non-negative values and the known end to the opening date or later.
- Require a non-empty source reference and reason, recorded actor/session, recorded time, explicit backfill/fallback/import origin and lifecycle status.
- Permit historical canceled/corrected rows while enforcing at most one active opening state per membership with a PostgreSQL partial unique index.
- Keep opening-state commands, permissions, idempotency, business audit, cache recalculation integration, adjustments, entry-batch storage and UI outside this storage-only step.

Scope:

- Add internal `MembershipOpeningStateRecord` and assembly-discovered EF configuration under the Memberships persistence boundary.
- Store `opening_as_of_date`, signed `declared_remaining_visits`, consistent `declared_negative_balance`, optional known effective end/extension, source reference, reason, actor/session, `recorded_at`, entry origin, optional future batch id and status.
- Restrict entry origin to `manual_backfill`, `paper_fallback` or reserved `future_import`; ordinary `normal` facts cannot masquerade as opening states.
- Add restrictive foreign keys to `issued_memberships`, `accounts` and `sessions`; leave `entry_batch_id` without a premature foreign key until canonical batch storage exists.
- Add checks for declared negative consistency, optional extension/date validity, non-empty source/reason and narrow origin/status values.
- Add partial unique index `ux_membership_opening_states_active_membership` and a descending membership timeline index, plus actor/session FK indexes.
- Generate reversible migration `20260713111435_AddMembershipOpeningStates` and update the EF model snapshot.
- Add six PostgreSQL-backed tests covering migration shape, metadata round-trip, optional known state, checks, active-row lifecycle uniqueness and restrictive relationships.

Validation:

- Release infrastructure build passed with 0 warnings and 0 errors after adding the EF record/configuration.
- Focused `PostgreSqlMembershipOpeningStatesStorageTests` validation passed all 6 tests against Docker PostgreSQL.
- Idempotent migration SQL from `20260713100046_AddMembershipStateCache` through `20260713111435_AddMembershipOpeningStates` was generated at `/tmp/bodylife-membership-opening-states.sql` and reviewed for the expected table, seven checks, three restrictive foreign keys, timeline/FK indexes, active partial uniqueness and migration-history transaction.
- Targeted whitespace formatting normalized the EF-generated migration files; final solution formatting/analyzer validation passed.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift.
- `scripts/apply-migrations.sh` applied `20260713111435_AddMembershipOpeningStates` to the local Docker Development database through the normal forward EF workflow.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 74 core tests, 35 web tests, 169 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713111435_AddMembershipOpeningStates`.
- The Development app was restarted from the validated Release build and `/health/ready` returned `200 OK` against Docker PostgreSQL.
- `graphify update .` completed the structural rebuild with 3889 nodes, 7271 edges and 532 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): add opening state storage`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the Memberships-owned opening-state domain contract and calculation behavior for starting from a declared as-of state, with focused domain tests. Keep the command/persistence writer, authorization, idempotency, audit, cache rebuild integration, adjustments and UI outside that step.

## Step 61 - Membership opening-state domain baseline

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the Memberships-owned domain contract and deterministic calculation for an already-authorized active opening-state source fact.
- Treat declared remaining visits as the honest as-of baseline when earlier history is incomplete; do not infer or generate synthetic historical visit facts to make the catalog limit reconcile.
- Derive and validate negative balance centrally from signed remaining visits while preserving negative opening states as normal Memberships state.
- Keep first-negative visit id/date and last-counted-visit time unknown when no canonical visit source establishes them, even when the declared baseline is negative.
- Resolve known effective end and known extension days against the immutable issued base end: derive the missing value when only one is known, require consistency when both are known and retain the base end/zero extension when neither is known.
- Require the opening date to be on or after membership start and on or before the resolved effective end, preserving the accepted inclusive active-date rule and active-membership-only backfill boundary.
- Keep persistence loading/writing, cache rebuild integration, command permissions/idempotency, business audit, adjustments, later visits/extensions and UI outside this domain-only step.

Scope:

- Add immutable `MembershipOpeningState` with `FromDeclaration` for command-side construction and `FromStoredSource` for drift-detecting source rehydration.
- Store only calculation inputs: opening as-of date, declared signed remaining/negative values and optional known effective-end/extension state; actor/session/source/reason remain command/source-fact metadata rather than formula inputs.
- Reject inconsistent persisted negative balance, unrepresentable `int.MinValue` negative state, negative known extension and a known end before the opening date.
- Add `MembershipStateCalculator.CalculateFromOpeningState` without changing the existing new-issue initial calculation path.
- Resolve effective end with overflow protection and reject shortening the canonical base term, mismatched known end/extension or an opening date outside the resolved active interval.
- Initialize calculated counted visits to zero without inventing old visits; copy declared signed balance and leave first-negative/last-visit metadata null until canonical visit facts exist.
- Add 16 focused domain cases covering declaration/source rehydration, negative consistency, metadata validation, honest positive/negative baselines, known date/extension derivation, mismatch/shortening/overflow rejection, inclusive interval checks, null inputs and immutability.

Validation:

- Release domain-project build passed with 0 warnings and 0 errors after adding the contract and calculator path.
- Focused `MembershipOpeningStateTests` validation passed all 16 cases.
- Wider `FullyQualifiedName~Memberships` core regression passed all 36 tests.
- Solution formatting verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this domain-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 90 core tests, 35 web tests, 169 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713111435_AddMembershipOpeningStates`.
- The Development app was restarted from the validated Release build and `/health/ready` returned `200 OK` against Docker PostgreSQL.
- `graphify update .` completed the structural rebuild with 3921 nodes, 7338 edges and 533 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): calculate opening state baseline`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Extend only `MembershipStateCacheRebuilder` to load the single active `membership_opening_states` source row, rehydrate the new domain contract and create/verify/repair cache state from that baseline inside its existing transaction and row lock. Keep opening-state commands, authorization, idempotency, audit, adjustments, later visit/extension inputs, public queries and UI outside that step.

## Step 62 - Opening-state-aware membership cache rebuild

Status: completed.

Plan alignment:

- Continue Milestone 5 by extending only the existing Memberships-owned cache rebuild path with the active opening-state source fact implemented in Steps 60-61.
- Acquire PostgreSQL `FOR UPDATE` on the canonical issued membership before reading the active opening state, preserving one parent lock order for source-writing commands and concurrent rebuilds.
- Read only `status = 'active'`; the existing partial unique index guarantees at most one applicable source while canceled/corrected rows remain historical facts.
- Rehydrate persisted opening values through `MembershipOpeningState.FromStoredSource` and calculate through Memberships domain ownership rather than mapping formulas in infrastructure.
- Preserve initial issue calculation when no active opening state exists and repair cache back to that state when an opening row is retired.
- Keep standalone `ReadCommitted` transaction ownership and existing outer-transaction participation/rollback behavior unchanged.
- Bump `CurrentRecalculationVersion` from 1 to 2 because the deterministic source-input contract now includes opening states; old-version cache rows become explicit repair candidates.
- Keep opening-state source-writing commands, authorization, idempotency, audit, adjustments, later visit/extension inputs, public queries and UI outside this rebuild-only step.

Scope:

- Rename the no-longer-initial-only entry point from `RebuildInitialAsync` to `RebuildAsync`; no application caller existed outside the PostgreSQL rebuild suite.
- Query the active `MembershipOpeningStateRecord` without tracker reuse after locking and rehydrate opening date, declared signed balance and optional known end/extension values.
- Select `CalculateInitial` when no active opening source exists or `CalculateFromOpeningState` when one does, then reuse the existing all-field compare/create/verify/repair write path.
- Leave source/accountability metadata out of formulas while retaining it in the canonical opening-state row for future command/audit/history workflows.
- Expand the PostgreSQL rebuild suite from 7 to 12 tests with active positive baseline creation, negative baseline repair without synthetic visit metadata, ignored historical rows, cross-source domain-invalid rollback and retirement back to issued initial state.
- Run existing concurrency and outer-transaction rollback tests with an active opening source, proving one cache row and declared baseline state under the same transaction behavior.
- Update version-drift coverage to repair version 1 into current calculation contract version 2.
- Add no EF model or migration change.

Validation:

- Release infrastructure-project build passed with 0 warnings and 0 errors after adding the active-source query and calculator selection.
- Focused `PostgreSqlMembershipStateCacheRebuildTests` validation passed all 12 tests against Docker PostgreSQL.
- Solution formatting verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; no migration was generated.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 90 core tests, 35 web tests, 174 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713111435_AddMembershipOpeningStates`.
- The Development app was restarted from the validated Release build and `/health/ready` returned `200 OK` against Docker PostgreSQL.
- `graphify update .` completed the structural rebuild with 3930 nodes, 7395 edges and 539 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): rebuild cache from opening state`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the public `CreateMembershipOpeningState` command contract and focused core tests for required common envelope/backfill metadata, declared state, permission intent, idempotency key and canonical reread target. Keep the persistence handler, transaction, source/cache writes, audit entry, entry-batch storage and UI outside that step.

## Step 63 - Membership opening-state command contract

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the public application command contract after the opening-state storage, domain calculation and cache-rebuild foundations from Steps 60-62.
- Reuse the accepted common `CommandEnvelope` for actor/account/role, session/device, request correlation, entry origin, business occurrence time, idempotency key, reason and comment instead of introducing duplicate opening-state metadata.
- Carry the honest opening declaration separately: target membership, opening as-of date, signed remaining visits, optional known effective end/extension, source reference and optional future batch reference.
- Keep declared negative balance out of command input because Memberships derives it centrally from signed remaining visits and the source writer will persist the validated result.
- Declare Admin + Owner permission intent in line with the accepted active-membership manual-backfill boundary and ADR-012; enforcement remains a server-side handler responsibility.
- Target the canonical Memberships reread after success rather than a source-row-shaped UI result or optimistic client-side state.
- Keep handler validation/authorization, PostgreSQL transaction and row locks, source/cache writes, idempotency persistence, business audit, entry-batch storage and UI outside this contract-only step.

Scope:

- Add public `CreateMembershipOpeningStateCommand` implementing `IBodyLifeCommand` with the common envelope and opening-state declaration/source fields.
- Keep known effective end, known extension days and `EntryBatchId` nullable so incomplete historical knowledge stays explicit and the future batch table is not pulled forward.
- Expose deterministic `CanonicalRereadTargetId` as the target `membership` identity for the future handler's common `CommandResult`.
- Add stable `MembershipActionKeys.CreateOpeningState` and `BodyLife.AdminOrOwner` policy constants for future query/UI permission projection and handler tests.
- Add five focused core contract tests covering the operational envelope/backfill metadata, idempotency key, declared signed state without duplicated negative balance, optional known/batch fields, permission intent and canonical reread target.
- Add no EF record/configuration/migration, infrastructure handler, dependency registration, audit row, source/cache mutation or UI change.

Validation:

- Focused `MembershipOpeningStateCommandContractsTests` validation passed all 5 cases.
- Wider `FullyQualifiedName~Memberships` core regression passed all 41 tests.
- Solution formatting verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this contract-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 95 core tests, 35 web tests, 174 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713111435_AddMembershipOpeningStates`.
- The Development app was restarted from the validated Release build and `/health/ready` returned `200 OK` against Docker PostgreSQL.
- `graphify update .` completed the structural rebuild with 3950 nodes, 7435 edges and 543 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): define opening state command`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Implement only the persistence-backed `CreateMembershipOpeningState` handler with canonical Admin/Owner authorization, backfill-envelope/source validation, active-membership row locking, idempotency, one PostgreSQL transaction for the opening source, Memberships cache rebuild and append-only business audit, then return the command's canonical membership reread target. Add focused PostgreSQL command tests; keep entry-batch storage and UI outside that step.

## Step 64 - Transactional membership opening-state command

Status: completed.

Plan alignment:

- Continue Milestone 5 by implementing only the persistence-backed workflow for the Step 63 `CreateMembershipOpeningState` public contract.
- Enforce the accepted ADR-010 boundary: this command accepts only explicit `manual_backfill` opening state for an existing active issued membership and never generates synthetic visits or patches derived cache values directly.
- Require the common command accountability fields before database work: canonical actor/session shape, request correlation id, idempotency key, `occurred_at`, non-empty reason and source reference; keep server `recorded_at` under `TimeProvider` control.
- Authorize Owner, named Admin and shared Reception/Admin through both command actor shape and canonical active account/unexpired-session rows, preserving honest shared-session accountability.
- Serialize competing writers with PostgreSQL `FOR UPDATE` on the issued membership and recheck idempotency after the lock, so concurrent same-key replays remain deterministic and different-key attempts cannot create two active opening sources.
- Commit the opening source, Memberships-owned synchronous cache rebuild, append-only business audit and successful idempotency result in one `ReadCommitted` transaction; any later failure rolls back earlier source/cache writes.
- Return the opening source as primary entity and the affected membership as the canonical reread target, without returning optimistic calculated UI state.
- Keep entry-batch table/FK storage, opening-state correction/cancellation, public membership queries and UI outside this command-handler step.

Scope:

- Add `CreateMembershipOpeningStateCommandHandler` and register it through `AddBodyLifePersistence` as the public command handler implementation.
- Add narrow `MembershipCommandSupport` for Admin/Owner actor authorization, normalized backfill validation, domain declaration construction, SHA-256 request fingerprinting, idempotent replay, PostgreSQL conflict mapping and common command results.
- Reject `normal`, `paper_fallback` and reserved `future_import` origins for this v1 opening workflow; require `manual_backfill`, `occurred_at`, reason, source reference and a non-empty optional batch UUID.
- Reuse `MembershipOpeningState.FromDeclaration`, issued snapshot/date contracts and `MembershipStateCalculator.CalculateFromOpeningState` for cross-source validation before mutation.
- Lock and require `issued_memberships.status = active`; return stable `not_found`, `membership_not_eligible`, `validation_failed`, `stale_state`, `duplicate_submission` or `concurrency_conflict` results where applicable.
- Persist the normalized opening source, derive negative balance centrally, call `MembershipStateCacheRebuilder` inside the existing transaction and write `membership_opening_state.created` audit with client/membership refs plus declared/recalculated summaries.
- Store successful idempotency with distinct primary opening-state id and canonical membership reread id; replay reconstructs the same common `CommandResult`.
- Add nine PostgreSQL-backed command tests covering canonical Named Admin creation and metadata, Owner/shared-account access, forged/expired/inactive/unknown denial, envelope/source/domain validation, missing/non-active/cross-source invalid membership cases, existing-active stale state, replay/payload mismatch, concurrent writers and rollback after audit failure.
- Add no EF model/configuration/migration, `entry_batches` table, page/controller or Playwright workflow.

Validation:

- Release infrastructure-project build passed with 0 warnings and 0 errors after adding the handler foundation.
- Focused `PostgreSqlCreateMembershipOpeningStateCommandTests` validation passed all 9 cases against Docker PostgreSQL.
- Wider `FullyQualifiedName~Membership` PostgreSQL regression passed all 76 tests, including storage, cache rebuild and MembershipType workflows.
- Wider `FullyQualifiedName~Memberships` core regression passed all 41 tests.
- Solution formatting verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this workflow uses the existing opening/cache/audit/idempotency schema.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 95 core tests, 35 web tests, 183 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713111435_AddMembershipOpeningStates`.
- The Development app was restarted from the validated Release build and `/health/ready` returned `200 OK` against Docker PostgreSQL.
- `graphify update .` completed the structural rebuild with 4040 nodes, 7731 edges and 541 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): create opening states transactionally`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the public `GetMembershipState` query/read-model contract with actor context, membership selector, `as_of` date, complete Memberships-owned stable state fields, permission intent and focused core contract tests. Keep the PostgreSQL query handler, warnings/history/extension rows, profile composition and UI outside that step.

## Step 65 - Membership state public query contract

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the public Memberships query/read-model boundary after the issued-membership, derived-cache and opening-state command foundations from Steps 57-64.
- Use a direct membership id selector in this first query path; defer client/current-membership selection until the accepted multiple-active-memberships product decision can define ambiguity honestly.
- Require an explicit `as_of` date so date-dependent state is deterministic and does not become an independent stored fact.
- Expose the immutable issue snapshot, start/base/effective dates and every currently available stable Memberships-owned derived field without moving formulas into profile, Reports or UI contracts.
- Derive active-by-date inside the Memberships read model through the existing inclusive `MembershipDateRules` rather than accepting a caller-computed boolean.
- Project allowed Memberships actions through the established `QueryPermissionSet` and stable Admin/Owner policy contract; handler-side actor/session enforcement remains the next persistence step.
- Keep PostgreSQL loading/registration, client/current selector resolution, warnings, extension explanation rows, history/drill-down composition, profile integration and UI outside this contract-only step.

Scope:

- Add `GetMembershipStateQuery` implementing `IBodyLifeQuery<GetMembershipStateResult>` with actor context, direct membership id and required `AsOfDate`.
- Add immutable `MembershipStateReadModel` fields for membership/client/type identity, issued snapshot, start/base/effective dates, counted/remaining/negative visits, first-negative visit id/date, extension days and last counted visit.
- Add Memberships-owned `IsActiveByDate`, computed from `AsOfDate` and `EffectiveEndDate` with the accepted inclusive date rule.
- Add `GetMembershipStateStatus` and result factories for success, permission denial, missing membership and validation failure, with no state or allowed actions leaked on failures.
- Carry `QueryPermissionSet` on successful results so the future handler can project the existing `memberships.create_opening_state` action under `BodyLife.AdminOrOwner` without embedding permission logic in UI.
- Add seven focused core contract cases covering actor/selector/date input, complete stable state shape, immutable properties, inclusive active-date behavior, successful permission projection and stable failure contracts.
- Add no EF record/configuration/migration, query handler, dependency registration, warning/history/extension-row type, profile composition or UI change.

Validation:

- Focused `MembershipStateQueryContractsTests` validation passed all 7 cases.
- Wider `FullyQualifiedName~Memberships` core regression passed all 48 tests.
- Release solution build passed with 0 warnings and 0 errors.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this public-contract step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 102 core tests, 35 web tests, 183 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713111435_AddMembershipOpeningStates`.
- `graphify update .` completed the structural rebuild with 4071 nodes, 7790 edges and 542 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): define state query contract`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Implement only the PostgreSQL-backed direct-id `GetMembershipState` query handler with canonical Admin/Owner actor/session authorization, input validation, issued snapshot plus current stable cache loading, Memberships-owned active-date mapping, allowed-action projection and focused PostgreSQL query tests. Keep client/current selection, warnings, extension explanation rows, history/profile composition and UI outside that step.

## Step 66 - PostgreSQL membership state query handler

Status: completed.

Plan alignment:

- Continue Milestone 5 by implementing only the persistence-backed direct-id handler for the Step 65 public `GetMembershipState` contract.
- Enforce query access for canonical active Owner, named Admin and shared Reception/Admin account/session rows before validating selectors or reading membership data.
- Keep the query read-only: do not rebuild/repair cache state, update source facts, append business audit or create command idempotency rows while serving a read.
- Read immutable issued snapshot fields and the current stable `membership_state_cache` row through the owning Memberships persistence boundary.
- Require the cache recalculation contract version to match `MembershipStateCacheRebuilder.CurrentRecalculationVersion`; distinguish a missing membership from missing/stale derived state.
- Rehydrate snapshot/base-date invariants through existing Memberships domain contracts and derive active-by-date only in the public read model for the requested `as_of` date.
- Project `CreateMembershipOpeningState` only for an active issued membership without an existing active opening source; do not let UI infer action eligibility.
- Keep client/current-membership selector resolution, warnings, extension explanation rows, history/profile composition and UI outside this handler-only step.

Scope:

- Add `GetMembershipStateQueryHandler` with canonical actor/session authorization, required membership-id/as-of validation and no-tracking PostgreSQL reads.
- Join `issued_memberships` to `membership_state_cache`, map all Step 65 stable fields and use an active-opening existence check solely for allowed-action projection.
- Add narrow `MembershipQuerySupport`, reusing the established Memberships command actor-shape/canonical-session checks in the same pattern as existing Clients query support.
- Add `RecalculationFailed` query status/result with stable `recalculation_failed` error semantics when the issued membership exists but its cache is missing, stale-versioned or cannot be safely rehydrated.
- Register the handler as scoped `IBodyLifeQueryHandler<GetMembershipStateQuery, GetMembershipStateResult>` through `AddBodyLifePersistence`.
- Add one core result-contract case and six focused infrastructure cases covering all accepted operational roles, complete opening-derived state, inclusive date behavior, action eligibility, denied actor/session shapes, validation/not-found errors, missing/stale cache without read-time repair, no audit/idempotency side effects and scoped DI registration.
- Add no EF record/configuration/migration, cache write/rebuild-on-read behavior, warning/history/extension-row type, profile composition or UI change.

Validation:

- Focused `MembershipStateQueryContractsTests` validation passed all 8 cases.
- Focused `PostgreSqlGetMembershipStateQueryTests` validation passed all 6 cases against Docker PostgreSQL.
- Wider `FullyQualifiedName~Memberships` core regression passed all 49 tests.
- Wider `FullyQualifiedName~Membership` PostgreSQL regression passed all 82 tests.
- Release infrastructure-project build passed with 0 warnings and 0 errors.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this read-only handler step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 103 core tests, 35 web tests, 189 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713111435_AddMembershipOpeningStates`.
- `graphify update .` completed the structural rebuild with 4119 nodes, 7918 edges and 525 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): query canonical membership state`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the Memberships-owned warning code/read-model contract and deterministic warning derivation for negative, zero, expired-by-date, ending-soon and low-remaining state using the accepted `as_of`, `days_left <= 7` and `remaining_visits <= 2` rules, with focused core tests. Keep PostgreSQL warning projection, extension explanation rows, client/profile composition and UI outside that step.

## Step 67 - Membership state warning rules

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the Memberships-owned warning contract and deterministic domain derivation needed by the canonical state query.
- Keep warnings as read-time state derived from `membership_state_cache` values plus the requested `as_of` date; do not persist warnings or let UI/Reports recreate thresholds.
- Apply the accepted ending-soon threshold on the inclusive date axis: 0 through 7 days left is `ending_soon`, while a negative day count is the distinct `expired_by_date` danger state.
- Apply the accepted visit threshold with specialized states: negative balance takes precedence over low remaining, zero remaining is explicit, and low remaining means the stated 1-2 positive visits.
- Allow one date-axis and one visit-axis warning to coexist so an expired membership can still expose low, zero or negative visit state independently.
- Order danger warnings before warning-severity states for deterministic reception rendering while leaving command-specific acknowledgement policy to later workflows.
- Derive from `MembershipCalculatedState` so warning logic consumes Memberships-owned canonical calculations rather than accepting caller formulas.
- Keep `GetMembershipState` read-model/handler projection, PostgreSQL changes, extension explanation rows, client/profile composition and UI outside this domain-only step.

Scope:

- Add stable `MembershipWarningCodes` for negative balance, expired by date, zero remaining, ending soon and low remaining.
- Add `MembershipWarningSeverity` with the accepted `warning` and `danger` semantics from the UI design foundation.
- Add immutable `MembershipWarning` read-model values with stable code, severity and server-provided message.
- Add `MembershipWarningRules.Derive` with fixed `EndingSoonDaysThreshold = 7` and `LowRemainingVisitsThreshold = 2`, required state/as-of validation and deterministic warning ordering.
- Suppress redundant `low_remaining` when the stronger zero/negative visit state applies and suppress `ending_soon` after expiry, without suppressing independent date-versus-visit warnings.
- Add 13 focused domain cases covering negative/zero/1/2/3 visit boundaries, 8/7/0/-1 date boundaries, danger ordering, expired-plus-low coexistence, stable contracts/messages, immutable properties and invalid inputs.
- Add no public query/read-model property, handler/DI change, EF record/configuration/migration, profile integration or UI change.

Validation:

- Focused `MembershipWarningRulesTests` validation passed all 13 cases.
- Wider `FullyQualifiedName~Memberships` core regression passed all 62 tests.
- Release solution build passed with 0 warnings and 0 errors.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this domain-only warning step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 116 core tests, 35 web tests, 189 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713111435_AddMembershipOpeningStates`.
- `graphify update .` completed the structural rebuild with 4147 nodes, 7963 edges and 553 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): derive state warnings`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Project the Step 67 warnings through only the public `GetMembershipState` read path: add Memberships-owned canonical cache-state rehydration/factory support, expose immutable `Warnings` on `MembershipStateReadModel`, and update the PostgreSQL handler plus focused core/infrastructure tests. Keep extension explanation rows, client/current selection, profile composition and UI outside that step.

## Step 68 - Membership warning projection through canonical state query

Status: completed.

Plan alignment:

- Continue Milestone 5 by projecting the Step 67 Memberships-owned warnings through only the existing direct-id `GetMembershipState` read path.
- Rehydrate `membership_state_cache` values through a Memberships domain factory before constructing a public read model, so Infrastructure does not recreate balance or date invariants.
- Use validated `MembershipIssueTerms` as the single source for membership type identity, immutable snapshot and inclusive start/base-end dates in the read model.
- Derive warnings at read time from the rehydrated canonical state plus the explicit query `as_of` date; do not persist warnings or derive thresholds in Infrastructure, profile, UI or Reports.
- Keep the query read-only and return the existing `recalculation_failed` result when a current-version cache row is internally inconsistent instead of repairing it during a read.
- Keep extension explanation rows, client/current-membership selection, profile composition, IssueMembership behavior and UI outside this step.

Scope:

- Add `MembershipCalculatedState.FromStoredCache` with required issue terms and validation for non-negative counted visits, signed remaining/negative balance consistency, non-negative extension days, supported calendar range and effective-end-date consistency.
- Refactor `MembershipStateReadModel` to consume canonical `MembershipIssueTerms` plus `MembershipCalculatedState` and expose a defensive read-only `Warnings` collection.
- Update `GetMembershipStateQueryHandler` to rehydrate cache values through the Memberships factory and project server-owned warnings without adding a query write or dependency-registration change.
- Add eight focused core stored-cache factory cases covering complete rehydration, numeric invariant failures, effective-end drift, calendar overflow and missing issue terms.
- Extend the public query contract tests to prove warning projection, danger ordering, immutable warning values/collection and unchanged stable state fields.
- Extend the PostgreSQL query suite to prove accepted actors receive warnings, `as_of` changes ending/expired warnings without cache writes, and an inconsistent current-version cache returns `recalculation_failed` without repair.
- Add no EF record/configuration/migration, persisted warning field, extension explanation row, client/profile composition or UI change.

Validation:

- Focused `MembershipCalculatedStateStoredCacheTests|MembershipStateQueryContractsTests` validation passed all 16 cases.
- Focused `PostgreSqlGetMembershipStateQueryTests` validation passed all 7 cases against Docker PostgreSQL.
- Wider `FullyQualifiedName~Memberships` core regression passed all 70 tests.
- Wider `FullyQualifiedName~Membership` PostgreSQL regression passed all 83 tests.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this read-only projection step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 124 core tests, 35 web tests, 190 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713111435_AddMembershipOpeningStates`.
- `graphify update .` completed the structural rebuild with 4166 nodes, 8008 edges and 545 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): project state warnings`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the derived `membership_extension_days` PostgreSQL storage foundation required by Milestone 5: EF record/configuration, reviewable migration and focused PostgreSQL constraint/index/storage tests. Do not generate explanation rows, change recalculation/query behavior, add source-module rules or build profile/UI in that storage-only step.

## Step 69 - Membership extension-day PostgreSQL storage

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the accepted `membership_extension_days` derived-storage foundation needed for explainable extension recalculation.
- Preserve source attribution per calendar date so overlapping sources remain visible while later Memberships recalculation counts unique active dates instead of summing rows.
- Treat extension rows like `membership_state_cache`: rebuildable derived data that can be deleted/recreated independently and cascades with its issued membership, not a new source-of-truth workflow.
- Keep source-type values extensible until the owning Freezes, NonWorkingDays and adjustment source contracts define canonical storage literals; require only non-empty source metadata in this storage step.
- Add indexes for active membership/date union reads and source lookup without adding report formulas or a persisted aggregate beside the existing Memberships-owned cache.
- Keep row generation/rebuild behavior, domain union calculation, `GetMembershipState` explanation projection, source-module tables/commands, profile composition and UI outside this step.

Scope:

- Add `MembershipExtensionDayRecord` with id, membership id, extension date, source type/id/label, active marker and recalculation timestamp.
- Add EF configuration for required date/source metadata, 64-character source type, 500-character source label and non-empty source type/label checks.
- Add a cascade FK to `issued_memberships`, a unique `(membership_id, extension_date, source_type, source_id)` source-day identity, a partial active `(membership_id, extension_date)` index and a `(source_type, source_id)` lookup index.
- Generate and review migration `20260713144951_AddMembershipExtensionDays` plus its designer and model snapshot update; the migration creates only the expected derived table, checks, FK and indexes.
- Add four PostgreSQL storage cases covering exact schema/index shape, overlapping sources with distinct active-date counting, metadata/identity constraint failures, unknown membership rejection, independent delete/rebuild and membership cascade behavior.
- Add no domain calculator, cache recalculation write, source-type allowlist, query/read-model property, business audit entry, profile integration or UI change.

Validation:

- Focused `PostgreSqlMembershipExtensionDaysStorageTests` validation passed all 4 cases against Docker PostgreSQL.
- Wider `FullyQualifiedName~Membership|FullyQualifiedName~PostgreSqlMigrationTests` PostgreSQL regression passed all 88 tests.
- Generated migration SQL was reviewed and contains only the expected table, two checks, cascade FK, two ordinary indexes, one unique index and migration-history row.
- Solution formatting/analyzer verification passed after removing the BOM emitted by `dotnet-ef` from the non-generated migration class.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 124 core tests, 35 web tests, 194 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713144951_AddMembershipExtensionDays`.
- `dotnet-ef database update 20260713144951_AddMembershipExtensionDays` applied the migration successfully to the local Docker development database `bodylife_crm_dev`.
- `graphify update .` completed the structural rebuild with 4218 nodes, 8139 edges and 551 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): add extension day storage`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the Memberships-owned domain contract and deterministic union calculator for active extension source dates, with focused tests for inclusive ranges, overlapping Freeze/NonWorkingDay/adjustment sources, inactive sources and stable explanation attribution. Keep PostgreSQL row generation, cache rebuild integration, source-module persistence, query projection and UI outside that domain-only step.

## Step 70 - Membership extension date union rules

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the Memberships-owned domain contract and deterministic union calculation required by the Step 69 derived extension-day storage.
- Expand every source range with the accepted inclusive `DateRange` semantics and count extension days as the union of distinct active calendar dates, never as a naive sum of overlapping ranges.
- Preserve one explanation entry per source and date so overlapping Freeze, NonWorkingDay and future adjustment projections remain attributable.
- Retain inactive source attribution in the calculation result while excluding those dates from the active union count, allowing later rebuilds to explain canceled or corrected source projections.
- Keep source types extensible until their owning modules define canonical literals and reject duplicate `(source_type, source_id)` projections before calculation.
- Produce immutable, deterministically ordered output so persistence and query boundaries do not need to recreate Memberships formulas or ordering rules.
- Keep PostgreSQL row replacement, cache rebuild integration, source-module persistence, `GetMembershipState` explanation projection, profile composition and UI outside this domain-only step.

Scope:

- Add validated `MembershipExtensionSourceRange` projections with stable source identity, trimmed bounded metadata, an inclusive date range and active state.
- Add immutable `MembershipExtensionDay` explanation values and `MembershipExtensionCalculation` results with a defensive read-only collection.
- Add `MembershipExtensionCalculator.Calculate` to expand inclusive ranges, retain every source/date attribution, count unique active dates and order rows by date, active state, source type, source id and label.
- Reject missing source collections/items, empty source ids, missing or oversized source metadata and duplicate source type/id identities.
- Add 13 focused core cases covering inclusive edges, three-way overlap, inactive attribution, empty input, metadata validation and trimming, duplicate identities, extensible source types, immutability and the `DateOnly.MaxValue` boundary.
- Add no EF record/configuration/migration, PostgreSQL writer/rebuilder, state-cache integration, query/read-model property, source-module contract, profile integration or UI change.

Validation:

- Focused `MembershipExtensionCalculatorTests` validation passed all 13 cases.
- Wider `FullyQualifiedName~Memberships` core regression passed all 83 tests.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this domain-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 137 core tests, 35 web tests, 194 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713144951_AddMembershipExtensionDays`.
- `graphify update .` completed the structural rebuild with 4254 nodes, 8199 edges and 560 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): calculate extension date union`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only a Memberships persistence boundary that atomically replaces one membership's derived `membership_extension_days` rows from a supplied canonical `MembershipExtensionCalculation`, with focused PostgreSQL tests for replacement, overlap attribution, idempotent retry and rollback behavior. Keep source loading, state-cache integration, query projection, Freezes/NonWorkingDays persistence and UI outside that step.

## Step 71 - Transactional membership extension-day writer

Status: completed.

Plan alignment:

- Continue Milestone 5 by connecting the Step 70 canonical extension calculation only to the Step 69 derived PostgreSQL storage boundary.
- Treat `membership_extension_days` as replaceable derived state: delete the prior batch and insert the supplied immutable explanation rows atomically instead of patching individual rows as business facts.
- Lock the parent issued membership before replacement so concurrent rebuilds serialize and cannot leave a mixed explanation batch.
- Join an existing caller transaction when recalculation is already part of a command, while owning and committing a read-committed transaction for standalone replacement.
- Persist every overlapping and inactive explanation attribution from Memberships unchanged, while using the calculation's distinct active-date count only as result metadata.
- Keep formulas in the Memberships domain calculator; Infrastructure maps canonical values and does not count, merge or reinterpret extension dates.
- Keep source loading, opening-state/cache interaction, `GetMembershipState` explanation projection, Freezes/NonWorkingDays persistence, business audit and UI outside this persistence-only step.

Scope:

- Add scoped `MembershipExtensionDayWriter.ReplaceAsync` with required membership/calculation validation, parent membership row locking, tracked-row detachment for same-scope retries, set-based deletion and one-batch insertion.
- Add immutable `MembershipExtensionDayWriteResult` and stable `MembershipExtensionDayWriteStatus` values for missing membership and successful replacement outcomes.
- Stamp every row in one replacement with the same `TimeProvider` value and return canonical extension-day and persisted-row counts without exposing EF records.
- Return a non-success missing-membership result without creating derived rows; allow an empty canonical calculation to clear stale rows successfully.
- Register the writer as a scoped dependency through `AddBodyLifePersistence` for later same-transaction Memberships recalculation orchestration.
- Add seven focused cases covering input/missing-membership behavior, stale-row replacement, overlapping active and inactive attribution, empty clearing, same-context retry, caller-owned rollback, concurrent batch serialization and scoped DI registration.
- Add no EF record/configuration/migration, source projection loader, state-cache write, recalculation orchestration, query/read-model property, audit entry or UI change.

Validation:

- Focused `PostgreSqlMembershipExtensionDayWriterTests` validation passed all 7 cases against Docker PostgreSQL.
- Wider `FullyQualifiedName~Memberships` core regression passed all 83 tests.
- Wider `FullyQualifiedName~Membership` PostgreSQL regression passed all 94 tests.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this writer-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 137 core tests, 35 web tests, 201 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713144951_AddMembershipExtensionDays`.
- `graphify update .` completed the structural rebuild with 4299 nodes, 8316 edges and 549 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): replace derived extension days`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the Memberships-owned domain operation that applies a canonical `MembershipExtensionCalculation` to an already calculated membership baseline and derives `extension_days` plus `effective_end_date` with calendar-overflow checks, with focused tests. Keep opening-state cutover policy, PostgreSQL/cache orchestration, source loading, query explanation projection and UI outside that domain-only step.

## Step 72 - Membership extension state derivation

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the Memberships-owned state transition between the Step 70 canonical extension calculation and a future central recalculation orchestrator.
- Derive `extension_days` from the calculation's distinct active-date union and derive `effective_end_date` from the immutable issued `base_end_date`; never accept a caller-edited effective date.
- Preserve counted visits, signed remaining visits, negative balance, first-negative metadata and last-counted-visit metadata from the already calculated baseline without moving those formulas into extension processing.
- Require an extension-free baseline that still matches the issued base end date, preventing accidental compounding, stale-cache reuse or silent replacement of opening-state extension knowledge.
- Keep overlapping and inactive explanation-row semantics inside the canonical `MembershipExtensionCalculation`; the state operation consumes only its accepted union count.
- Protect the supported `DateOnly` calendar boundary and reject overflow rather than wrapping or truncating an effective end date.
- Keep opening-state cutover policy, PostgreSQL/cache orchestration, source loading, query explanation projection and UI outside this domain-only step.

Scope:

- Add `MembershipStateCalculator.ApplyExtensionCalculation` with required issue terms, baseline and calculation inputs.
- Validate that the baseline has zero extension days and an effective end matching the supplied issue terms before applying the canonical calculation.
- Return a new immutable `MembershipCalculatedState` that preserves all non-extension fields and sets only canonical extension days and derived effective end date.
- Reject calendar overflow with stable `extensionCalculation` parameter metadata while allowing an extension to reach `DateOnly.MaxValue` exactly.
- Add eight focused core cases covering overlapping active and inactive attribution, preservation of visit/negative metadata, baseline immutability, empty and inactive-only calculations, inclusive active-end behavior, exact calendar boundary, overflow, missing inputs, already-extended baseline and mismatched issue terms.
- Add no new contract type, EF record/configuration/migration, PostgreSQL/cache writer integration, opening-state policy, query/read-model property, source-module behavior or UI change.

Validation:

- Focused `MembershipStateExtensionCalculationTests` validation passed all 8 cases.
- Wider `FullyQualifiedName~Memberships` core regression passed all 91 tests.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` built successfully and reported no model drift; this domain-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 145 core tests, 35 web tests, 201 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713144951_AddMembershipExtensionDays`.
- `graphify update .` completed the structural rebuild with 4317 nodes, 8362 edges and 569 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): apply extension calculations`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the immutable Memberships-owned extension explanation item and collection to the public `GetMembershipState` read-model contract, with focused core contract tests for overlapping/inactive attribution shape, defensive collection copying and failure-result non-leakage. Keep PostgreSQL loading, cache/explanation consistency policy, client/profile composition and UI outside that contract-only step.

## Step 73 - Membership extension explanation query contract

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the public Memberships read-model shape required to expose explainable extension rows through canonical `GetMembershipState` queries.
- Reuse the Step 70 immutable `MembershipExtensionDay` value instead of creating a second DTO with duplicated source/date semantics.
- Expose extension date, stable source type/id/label and active state so overlapping sources and canceled/inactive attribution remain independently visible.
- Copy explanation rows defensively into an immutable collection owned by `MembershipStateReadModel`; caller collection mutation cannot change canonical query output.
- Default the collection to empty for the existing handler until the separate PostgreSQL projection step, preserving deployable behavior without pretending persistence loading already exists.
- Keep explanation rows separate from the aggregate `ExtensionDays`; this contract step does not add cache/explanation consistency policy or reinterpret imported opening-state extension knowledge.
- Preserve failure-result non-leakage: permission, validation, not-found and recalculation failures carry neither state/explanations nor allowed actions.

Scope:

- Add `MembershipExtensionDay.FromStoredExplanation` to hydrate one validated immutable item from stored date/source metadata while reusing existing source validation and normalization.
- Add `MembershipStateReadModel.ExtensionExplanation` as `IReadOnlyList<MembershipExtensionDay>` with defensive copying, a read-only wrapper and rejection of missing collection items.
- Keep the constructor compatible with the existing handler by treating an omitted explanation collection as empty until persistence projection is implemented.
- Extend `MembershipStateQueryContractsTests` from 8 to 11 cases with overlapping Freeze/NonWorkingDay attribution, inactive adjustment attribution, defensive source-list copying, read-only enforcement, stored metadata normalization/validation and failure-result non-leakage.
- Verify the successful result preserves its canonical explanation state while every established failure factory returns null state and empty action permissions.
- Add no PostgreSQL query, handler/DI change, EF record/configuration/migration, cache consistency rule, client/profile composition or UI rendering.

Validation:

- Focused `MembershipStateQueryContractsTests` validation passed all 11 cases.
- Wider `FullyQualifiedName~Memberships` core regression passed all 94 tests.
- Focused `PostgreSqlGetMembershipStateQueryTests` compatibility regression passed all 7 cases against Docker PostgreSQL without changing the handler.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this contract-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 148 core tests, 35 web tests, 201 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713144951_AddMembershipExtensionDays`.
- `graphify update .` completed the structural rebuild with 4324 nodes, 8383 edges and 555 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): expose extension explanations`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Project stored `membership_extension_days` through only the PostgreSQL `GetMembershipState` handler: load rows without tracking, order deterministically by date/active/source identity, hydrate canonical explanation items and add focused infrastructure tests for overlap, inactive attribution, empty rows and read-only query behavior. Keep cache/explanation consistency enforcement, client/profile composition, recalculation orchestration and UI outside that projection-only step.

## Step 74 - PostgreSQL membership extension explanation projection

Status: completed.

Plan alignment:

- Complete the Milestone 5 `GetMembershipState` output shape by projecting the Step 69 derived `membership_extension_days` rows through the Step 73 public explanation contract.
- Keep Memberships as the owner of explanation semantics by hydrating the existing immutable `MembershipExtensionDay` value instead of adding an Infrastructure DTO or recalculating extension dates in the query.
- Load explanation rows only after actor authorization, membership/cache lookup and recalculation-version validation have succeeded.
- Preserve every overlapping and inactive attribution row and order results deterministically by extension date, active rows first, source type, source id and source label.
- Keep the aggregate `ExtensionDays` sourced from `membership_state_cache`; this projection does not impose cache/explanation equality because an honest opening state can carry known extension state without reconstructed historical explanation rows.
- Keep the query read-only with no tracking, no repair-on-read, no business audit and no idempotency writes.
- Keep client/profile composition, central recalculation orchestration, source loading, cache/explanation write consistency and UI outside this projection-only step.

Scope:

- Extend `GetMembershipStateQueryHandler` with one no-tracking projection over the selected membership's stored extension-day rows.
- Rehydrate each stored date/source tuple through `MembershipExtensionDay.FromStoredExplanation`; invalid stored metadata follows the existing stable `recalculation_failed` path instead of leaking malformed canonical state.
- Pass the ordered immutable explanation collection into `MembershipStateReadModel` without changing its contract or persistence registration.
- Extend the opening-state query case to prove that a valid cache can still return an empty explanation collection.
- Add one focused PostgreSQL case with overlapping active freezes, a non-working period and an inactive adjustment inserted out of order; verify canonical ordering, all source metadata, the read-only collection wrapper and absence of query side effects.
- Add no EF record/configuration/migration, cache writer, source provider, transaction coordinator, profile/UI projection or new authorization behavior.

Validation:

- Focused `PostgreSqlGetMembershipStateQueryTests` validation passed all 8 cases against Docker PostgreSQL.
- Wider `FullyQualifiedName~Memberships` core regression passed all 94 tests.
- Wider `FullyQualifiedName~Membership` PostgreSQL regression passed all 95 tests.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this projection-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 148 core tests, 35 web tests, 202 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713144951_AddMembershipExtensionDays`.
- `graphify update .` completed the structural rebuild with 4328 nodes, 8405 edges and 554 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): project extension explanations`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the Memberships persistence coordinator that atomically writes one already-canonical `MembershipCalculatedState` and matching `MembershipExtensionCalculation` to `membership_state_cache` plus `membership_extension_days`, with consistency validation, one parent lock, one recalculation timestamp and rollback-focused PostgreSQL tests. Keep source-fact loading, opening-state cutover policy, command/audit orchestration, client/profile composition and UI outside that write-boundary step.

## Step 75 - Atomic membership derived-state persistence coordinator

Status: completed.

Plan alignment:

- Continue Milestone 5 central recalculation infrastructure with only the write boundary between already-canonical Memberships calculations and the two rebuildable PostgreSQL projections.
- Accept a `MembershipCalculatedState` and its matching `MembershipExtensionCalculation`; do not load or reinterpret Visits, Freezes, NonWorkingDays, adjustments or opening-state cutover rules in Infrastructure.
- Reject aggregate/explanation mismatches before touching PostgreSQL and rehydrate the supplied state through Memberships domain validation against the locked issued snapshot and base end date.
- Lock the parent issued membership once, then upsert `membership_state_cache` and delete/rebuild `membership_extension_days` inside one read-committed transaction.
- Join an existing command transaction without committing it, or own and commit a transaction for standalone persistence.
- Stamp the cache and every explanation row with one `TimeProvider` value and the current recalculation version so readers cannot observe mixed derived batches after commit.
- Keep all formulas in the Memberships domain objects; the coordinator only validates, maps and persists canonical values.
- Keep source-fact loading, opening-state cutover policy, recalculation orchestration, business audit, client/profile composition and UI outside this persistence-only step.

Scope:

- Add scoped `MembershipStatePersistenceCoordinator.PersistAsync` with required input validation, stable missing-membership result and immutable success metadata.
- Add `MembershipStatePersistenceStatus` and `MembershipStatePersistenceResult` with canonical state, persisted explanation-row count, shared recalculation timestamp and recalculation version.
- Reuse the extension writer's replacement mapping through an internal after-lock primitive, preserving the standalone writer's existing public transaction behavior.
- Reuse the cache rebuilder's field mapping so both persistence paths stamp every stable cache field and version identically.
- Register the coordinator as scoped through `AddBodyLifePersistence` for later same-transaction recalculation orchestration.
- Add seven focused cases covering invalid/missing input, aggregate/explanation mismatch, full cache plus overlapping/inactive explanation persistence, state-to-issued-terms validation, same-scope clearing, caller-owned rollback, concurrent batch serialization and scoped DI registration.
- Add no EF record/configuration/migration, source provider, recalculation source loader, opening-state behavior, command/audit event, profile query or UI change.

Validation:

- The first focused test attempt stopped only on xUnit analyzer `xUnit2031` for a filtered `Assert.Single`; the assertion was changed to the predicate overload and no product behavior failed.
- Focused `PostgreSqlMembershipStatePersistenceCoordinatorTests` validation then passed all 7 cases against Docker PostgreSQL.
- Focused extension-writer and state-cache-rebuilder compatibility regression passed all 19 cases.
- Wider `FullyQualifiedName~Memberships` core regression passed all 94 tests.
- Wider `FullyQualifiedName~Membership` PostgreSQL regression passed all 102 tests.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this persistence-service step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 148 core tests, 35 web tests, 209 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713144951_AddMembershipExtensionDays`.
- `graphify update .` completed the structural rebuild with 4382 nodes, 8560 edges and 553 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): persist derived state atomically`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the remaining Milestone 5 `membership_adjustments` PostgreSQL source-fact storage: EF record/configuration, reviewable migration and focused constraint/index/migration tests for explicit dated adjustments with reason, actor and retained status history. Keep adjustment commands, recalculation source loading, audit orchestration, profile/history projection and UI outside that storage-only step.

## Step 76 - Membership adjustment source-fact storage

Status: completed.

Plan alignment:

- Complete the remaining Milestone 5 source-fact table named by the roadmap without adding an adjustment workflow that is not yet defined by a stable command contract.
- Store explicit dated membership adjustments separately from the rebuildable `membership_state_cache` and `membership_extension_days` projections; derived state remains replaceable while adjustment history remains canonical.
- Preserve signed optional day, visit and money deltas while requiring at least one non-zero delta, so PostgreSQL rejects empty correction facts without prematurely restricting a future domain-specific adjustment policy.
- Require adjustment type, effective date, reason, actor account, session, entry origin, recorded time and retained `active`, `canceled` or `corrected` status history.
- Keep the active lookup index non-unique because multiple legitimate adjustments can affect the same membership, date and adjustment type; idempotency belongs to the future command boundary.
- Use `RESTRICT` relationships to protect source history from deletion of the issued membership, actor account or recording session.
- Keep adjustment commands, type-policy semantics, recalculation source loading, business audit orchestration, profile/history projection and UI outside this storage-only step.

Scope:

- Add `MembershipAdjustmentRecord` with accepted roadmap fields plus the existing command-accountability convention for recording session, entry origin and optional entry batch.
- Add EF Core mapping for `bodylife.membership_adjustments`, bounded metadata, signed nullable deltas, PostgreSQL checks, retained status values and restrictive foreign keys.
- Add a filtered active recalculation lookup index, deterministic membership timeline index and actor/session support indexes.
- Add migration `20260713194005_AddMembershipAdjustments` and update the EF model snapshot.
- Add five focused PostgreSQL cases covering the exact migration shape, signed delta/accountability persistence, non-zero and metadata constraints, coexisting active/history facts and protected relationships.
- Add no public adjustment command, handler, domain adjustment-type enum, recalculation source reader, audit entry, query projection or UI change.

Validation:

- Focused `PostgreSqlMembershipAdjustmentsStorageTests` validation passed all 5 cases against Docker PostgreSQL.
- Wider `FullyQualifiedName~Membership` PostgreSQL regression passed all 107 tests.
- Wider `FullyQualifiedName~Memberships` core regression passed all 94 tests.
- Solution formatting/analyzer verification passed after removing the UTF-8 BOM emitted by the EF migration generator.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift.
- The generated SQL from `20260713144951_AddMembershipExtensionDays` to `20260713194005_AddMembershipAdjustments` was reviewed and contains only the expected table, checks, restrictive foreign keys, four indexes and migration-history insert.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 148 core tests, 35 web tests, 214 PostgreSQL infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260713194005_AddMembershipAdjustments`.
- `graphify update .` completed the structural rebuild with 4434 nodes, 8678 edges and 559 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): store membership adjustments`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the immutable `PreviewIssueMembership` public query contract and Memberships-owned pure preview policy for snapshot/base-end-date output, permission outcome and explicit existing-negative-state decision requirements, with focused core tests. Keep PostgreSQL loading, `IssueMembership`, payment integration, idempotency, audit, profile composition and UI outside that contract-only step.

## Step 77 - Membership issue preview contract and policy

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the public advisory `PreviewIssueMembership` shape and Memberships-owned pure policy required before implementing its PostgreSQL handler or the state-changing issue command.
- Build the proposed snapshot through `MembershipIssueTerms.FromActiveMembershipType`, preserving the immutable MembershipType copy and accepted inclusive `base_end_date = start_date + duration_days - 1 day` rule.
- Build expected initial state through `MembershipStateCalculator.CalculateInitial`; the preview does not reproduce Memberships formulas or edit effective end date directly.
- Keep existing negative state independently visible from the new membership's expected initial state, so a payment or fresh snapshot cannot silently hide old negative visits.
- Require an explicit decision whenever existing negative state is present; a missing decision returns a successful advisory preview that cannot proceed to issue.
- Expose `leave visible`, `cover with new membership` and `record explicit closure` as the three documented concepts while marking only `leave visible` available now. The vertical slice explicitly defers negative coverage and one-off closure, so selecting either deferred option cannot proceed.
- Allow an unknown first-negative date for honest opening-state/backfill history while preserving the positive negative balance and warning.
- Keep authorization execution, PostgreSQL reads, multiple-active-membership resolution, `IssueMembership`, payment integration, idempotency, audit, profile composition and UI outside this contract-only step.

Scope:

- Add `PreviewIssueMembershipQuery`, result/status contracts and stable failure factories for permission, missing client/type, inactive type, validation and unavailable canonical state.
- Add immutable `MembershipIssuePreview` output with snapshot, proposed/base dates, expected initial state, existing negative context, server-owned warning, decision options and explicit `RequiresNegativeHandlingDecision`/`CanProceedToIssue` outcomes.
- Add validated `MembershipIssueNegativeContext`, `MembershipNegativeHandlingDecision` and immutable option metadata.
- Add `MembershipIssuePreviewPolicy.Create` to copy active catalog terms, derive initial state and enforce the currently accepted negative-decision capability boundary.
- Add stable `memberships.issue` Admin/Owner permission intent to `MembershipActionKeys` for future handler projection.
- Add 14 focused core cases covering query shape, immutable snapshot/date/initial-state output, catalog independence, negative warning/decision requirements, deferred options, unknown first-negative date, validation/calendar boundaries, permission projection and failure non-leakage.
- Add no query handler/DI registration, EF record/configuration/migration, PostgreSQL read, issue command, payment/closure fact, idempotency row, business audit event, profile integration or UI change.

Validation:

- The first focused attempt stopped before test execution on nullable analysis in one test assertion; the assertion was changed to a typed non-null capture and no product behavior failed.
- Focused `MembershipIssuePreviewContractsTests` validation then passed all 14 cases.
- Wider `FullyQualifiedName~Memberships` core regression passed all 108 tests.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this contract/domain-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 162 core tests, 35 web tests, 214 PostgreSQL infrastructure tests, 24 Playwright smoke tests and unchanged EF migration listing through `20260713194005_AddMembershipAdjustments`.
- `graphify update .` completed the structural rebuild with 4489 nodes, 8787 edges and 568 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): define issue preview policy`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the PostgreSQL `PreviewIssueMembership` query handler: authorize the canonical actor/session, validate and load the client plus active MembershipType, project zero or one unambiguous existing negative membership state into the Step 77 policy, and fail clearly when multiple negative candidates require a product selection rule. Add focused PostgreSQL tests and scoped DI registration. Keep `IssueMembership`, payments/closure, idempotency, audit, profile composition and UI outside that handler-only step.

## Step 78 - PostgreSQL membership issue preview handler

Status: completed.

Plan alignment:

- Complete the Milestone 5 advisory `PreviewIssueMembership` boundary by connecting the Step 77 public contract and pure policy to canonical PostgreSQL reads only.
- Reuse `MembershipQuerySupport` for the established Owner, named Admin and shared Reception/Admin actor-shape plus active account/session authorization; forged, inactive, expired, ended and unknown sessions are denied before business data is returned.
- Validate required client/type selectors, proposed start date and negative-decision enum before loading preview data.
- Load the Client and current MembershipType without tracking, distinguish missing from inactive catalog rows and map the live catalog value only into the Step 77 immutable issue snapshot policy.
- Inspect every `active` issued membership for the selected client and require a present, current-version, domain-rehydratable `membership_state_cache`; missing, stale or inconsistent derived state returns `recalculation_failed` without repair-on-read.
- Build negative context only from a domain-validated cache state. Zero negative candidates produces ordinary preview, one produces the explicit decision/warning flow and multiple candidates return `validation_failed` instead of aggregating balances or inventing the unresolved multiple-membership policy.
- Ignore canceled historical memberships when resolving the current negative candidate while retaining their source/cache rows unchanged.
- Keep the query read-only with no audit or idempotency writes and expose `memberships.issue` only as permission intent; eligibility remains in the preview's decision outcome.
- Keep `IssueMembership`, payments/closure, source-fact mutation, recalculation writes, business audit, profile composition and UI outside this handler-only step.

Scope:

- Add `PreviewIssueMembershipQueryHandler` with canonical authorization, stable selector/catalog failures, no-tracking PostgreSQL reads and Memberships domain rehydration before preview policy execution.
- Add one all-active-membership cache integrity check so preview cannot silently miss a negative state because a derived row is absent or stale.
- Extend `MembershipQuerySupport` with immutable Admin/Owner issue-action permission projection.
- Register the public preview handler as scoped `IBodyLifeQueryHandler<PreviewIssueMembershipQuery, PreviewIssueMembershipResult>` through `AddBodyLifePersistence`.
- Add nine focused cases covering all accepted operational roles, canonical catalog snapshot output, single-negative decisions, unknown first-negative date, multiple-negative ambiguity and canceled-history exclusion, missing/stale/inconsistent cache without repair, stable selector/catalog/calendar failures, denied actor/session shapes, no query side effects and scoped DI registration.
- Add no EF record/configuration/migration, schema/index change, state mutation, issue command, payment/closure fact, idempotency row, audit entry, page/controller or UI test.

Validation:

- Focused `PostgreSqlPreviewIssueMembershipQueryTests` validation passed all 9 cases against Docker PostgreSQL.
- Wider `FullyQualifiedName~Membership` PostgreSQL regression passed all 116 tests.
- Wider `FullyQualifiedName~Memberships` core regression passed all 108 tests.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this handler-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 162 core tests, 35 web tests, 223 PostgreSQL infrastructure tests, 24 Playwright smoke tests and unchanged EF migration listing through `20260713194005_AddMembershipAdjustments`.
- `graphify update .` completed the structural rebuild with 4541 nodes, 8976 edges and 570 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): handle issue previews`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the public `IssueMembership` command/result contract and Memberships-owned pure preparation policy for immutable snapshot copying, inclusive base end date, explicit negative decision enforcement and expected initial state, with focused core tests. Keep the PostgreSQL command handler, payment/closure integration, transaction/idempotency/audit orchestration, profile reread and UI outside that contract-only step.

## Step 79 - Membership issue command contract and preparation policy

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the public state-changing `IssueMembership` boundary and Memberships-owned pure preparation required before PostgreSQL orchestration.
- Reuse the common `CommandEnvelope` for actor/session accountability, request correlation, entry origin, business occurrence time, idempotency, reason and comment; carry only the client/type/start selectors, explicit negative decision and optional future batch reference on the command.
- Keep snapshot, base end date and calculated state out of client input. The future handler must reload the canonical active MembershipType and create these values through Memberships at execution time.
- Use the existing common `CommandResult`: the newly issued membership is the primary entity and the client is the canonical reread target, so UI state must come from a post-commit client/profile query rather than optimistic command output.
- Add the documented `NegativeDecisionRequired` command error contract for the future handler instead of collapsing this distinct blocking condition into a generic validation error.
- Reuse `MembershipIssuePreviewPolicy` inside the preparation path so preview and mutation preparation cannot drift on immutable snapshot copying, inclusive base-end-date arithmetic, expected initial state, warning semantics or negative-decision availability.
- Permit ordinary issue without a negative decision when no existing negative state is present. When negative state exists, require an explicit decision and currently permit only `LeaveVisible`, preserving the old negative context and warning separately from the new membership's zero-negative initial state.
- Continue to expose cover-with-new-membership and explicit closure only in advisory preview metadata; both remain rejected by mutation preparation because the vertical slice defers those source-fact workflows.
- Keep payment/closure input and orchestration for the later Payments boundary, and keep authorization execution, canonical PostgreSQL reloads, transactions, row locks, idempotency storage, issued/cache writes, audit and UI outside this contract-only step.

Scope:

- Add public `IssueMembershipCommand` implementing `IBodyLifeCommand` with common envelope, client and MembershipType selectors, start date, optional negative decision, optional entry-batch reference and deterministic client canonical reread target.
- Declare stable `membership` primary-entity and `client` canonical-reread entity types for the future common command result.
- Add immutable `MembershipIssuePreparation` with canonical snapshot, inclusive base date, expected initial state, retained existing-negative context, selected decision and read-only warnings.
- Add `MembershipIssuePreparationPolicy.Prepare`, delegating canonical calculations to the accepted preview policy and rejecting missing or currently unavailable negative decisions.
- Add `CommandErrorCode.NegativeDecisionRequired` without renumbering existing command errors.
- Add 12 focused core cases covering command envelope/selectors/defaults/batch shape, absence of client-supplied derived state, common result identities, stable negative-decision error, immutable catalog snapshot, inclusive initial state, catalog independence, required/leave-visible/deferred decision behavior, read-only warnings and reused validation/calendar guards.
- Add no command handler/DI registration, EF record/configuration/migration, PostgreSQL mutation, payment/closure fact, idempotency row, business audit entry, profile composition, controller/page or UI change.

Validation:

- The first focused attempt stopped before test execution because a new test fixture used the wrong `DateTimeOffset` constructor arity; the fixture was corrected and no product behavior failed.
- Focused `IssueMembershipCommandContractsTests` validation passed all 12 cases.
- Wider `FullyQualifiedName~Memberships` core regression passed all 120 tests.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this contract/domain-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 174 core tests, 35 web tests, 223 PostgreSQL infrastructure tests, 24 Playwright smoke tests and unchanged EF migration listing through `20260713194005_AddMembershipAdjustments`.
- `graphify update .` completed the structural rebuild with 4577 nodes, 9057 edges and 573 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): define issue command preparation`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Implement only the PostgreSQL `IssueMembership` handler for the currently accepted no-payment path: canonical Admin/Owner authorization, envelope/idempotency validation, client and affected-membership locking, active MembershipType reload, canonical negative-state recheck through the Step 79 preparation policy, one transaction for issued snapshot plus initial cache, append-only `membership.issued` audit and common client reread result. Add focused PostgreSQL tests for permissions, inactive/missing selectors, required/deferred negative decisions, replay/payload mismatch, concurrency and rollback. Keep payment/closure facts, manual opening-state orchestration, profile composition and UI outside that handler-only step.

## Step 80 - Transactional PostgreSQL membership issue command

Status: completed.

Plan alignment:

- Continue Milestone 5 by implementing only the persistence-backed ordinary `IssueMembership` workflow after the Step 79 public command and pure preparation boundary.
- Accept only `normal` entry origin in this handler. Reject entry-batch/manual-backfill/fallback orchestration explicitly so the already accepted opening-state command remains the honest historical-state path and this step does not invent migration behavior.
- Require the common command accountability fields needed by the quick action: canonical actor/session shape, request correlation id and idempotency key; normalize bounded reason/comment/device metadata while keeping server `recorded_at` under `TimeProvider` control.
- Authorize Owner, named Admin and shared Reception/Admin against canonical active account and unexpired session rows before any business mutation.
- Serialize issue workflows per client with a PostgreSQL client `FOR UPDATE` lock, protect the selected MembershipType snapshot against concurrent edit/deactivation with `FOR SHARE`, then lock every active issued membership before rereading its cache-derived negative state.
- Recheck idempotency after the client lock so concurrent identical submits return the original membership/audit/client-reread result and cannot create duplicate source/cache/audit rows.
- Require a present, current-version and domain-rehydratable cache for every active existing membership. Missing, stale or inconsistent derived state fails with `recalculation_failed` and is not repaired as an issue side effect.
- Preserve the accepted ambiguity boundary: zero or one existing negative candidate can proceed through the Step 79 preparation policy, while multiple negative candidates return validation failure until an explicit selection policy is accepted.
- Reload the active MembershipType inside the transaction and copy its immutable snapshot through `MembershipIssuePreparationPolicy`; never accept snapshot, base end date or calculated state from the caller.
- Enforce explicit negative handling in the transaction: missing decision returns `NegativeDecisionRequired`, deferred coverage/closure returns `MembershipNotEligible`, and only `LeaveVisible` currently succeeds while preserving the old cache and returning the negative warning.
- Commit the issued source, Memberships-owned initial cache rebuild, append-only `membership.issued` audit and successful idempotency result in one `ReadCommitted` transaction. Recalculation mismatch/missing source and later audit failure roll back the entire workflow.
- Return the issued membership as primary entity and the client as canonical reread target, with warning codes only; do not return optimistic profile or calculated UI state.
- Keep payment/negative-closure facts, manual opening-state orchestration, client-profile composition, controller/page and reception UI outside this handler-only step.

Scope:

- Add `IssueMembershipCommandHandler` with canonical authorization, deliberate PostgreSQL locks, canonical catalog/negative-state reload, Step 79 preparation, issued source persistence, synchronous cache rebuild, audit, idempotency and common command result.
- Add narrow `IssueMembershipCommandSupport` for ordinary issue validation/normalization, SHA-256 request fingerprints, deterministic replay/payload mismatch, successful idempotency storage, warning replay, stable negative-decision labels and result identities.
- Add `membership` / `membership.issued` audit constants and scoped `IBodyLifeCommandHandler<IssueMembershipCommand>` registration through `AddBodyLifePersistence`.
- Add 14 focused infrastructure cases, including 13 PostgreSQL-backed workflows plus one scoped DI registration case, covering complete Named Admin persistence/audit metadata, Owner/shared-account access, forged/inactive/expired/unknown denial, selector/envelope/origin/batch/enum validation, missing/inactive catalog rows, unnecessary/required/deferred/leave-visible negative decisions, old-negative preservation, missing/stale/inconsistent caches without repair, multiple-negative ambiguity, calendar overflow, replay/payload mismatch, concurrent same-key serialization, recalculation rollback and audit rollback.
- Add no EF record/configuration/migration, schema/index change, payment or closure record, opening-state write, profile query composition, page/controller or UI test.

Validation:

- The first focused attempt stopped before test execution because the new test used a nonexistent `JsonElement.GetDateOnly`; assertions were changed to canonical ISO JSON strings and no product behavior failed.
- The next focused run passed 11 cases and failed two audit-read assertions because the test helper used EF property-style JSON column names instead of the existing PostgreSQL `related_entity_refs`, `before_summary` and `after_summary` columns; the helper was corrected without a product-code change.
- Final focused `PostgreSqlIssueMembershipCommandTests` validation passed all 14 cases: 13 PostgreSQL-backed workflows against Docker PostgreSQL and one DI registration check.
- Wider `FullyQualifiedName~Membership` PostgreSQL regression passed all 130 tests.
- Wider `FullyQualifiedName~Memberships` core regression passed all 120 tests.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this handler uses the existing issued/cache/audit/idempotency schema and generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 174 core tests, 35 web tests, 237 PostgreSQL infrastructure tests, 24 Playwright smoke tests and unchanged EF migration listing through `20260713194005_AddMembershipAdjustments`.
- `graphify update .` completed the structural rebuild with 4674 nodes, 9441 edges and 558 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): issue memberships transactionally`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the public client-scoped Memberships collection query contract and pure active-candidate selection outcome needed by `GetClientProfile`: actor, client id and required `as_of`; deterministic membership timeline; explicit `none`, `single` or `ambiguous` active-candidate status; and no arbitrary current-membership choice when multiple candidates exist. Reuse `MembershipStateReadModel` as the canonical state shape and add focused core tests. Keep the PostgreSQL collection handler, profile composition and UI outside that contract-only step.

## Step 81 - Client membership state collection contract

Status: completed.

Plan alignment:

- Continue Milestone 5 with only the public client-scoped Memberships collection boundary required before composing the membership section of `GetClientProfile`.
- Carry canonical actor/session context, required client id and required `as_of` date on `GetClientMembershipStatesQuery`; keep authorization and PostgreSQL execution for the next handler-only step.
- Reuse `MembershipStateReadModel` for every calculated membership value, warning and immutable issue snapshot so profile composition cannot duplicate Memberships formulas.
- Wrap each canonical state only with source-owned lifecycle status and `issued_at`, which are needed to exclude canceled/corrected history from active selection and to make timeline ordering deterministic.
- Define an active candidate as both lifecycle `active` and canonically active by date. Preserve the accepted inclusive Memberships date rule instead of introducing a profile-specific date formula.
- Return an explicit `none`, `single` or `ambiguous` active-candidate outcome. An ambiguous result exposes the ordered candidates but deliberately has no selected membership, preserving the unresolved multiple-active-memberships product decision.
- Order the timeline newest-first by start date, then issue time, then membership id as a stable final tie-break; validate client/as-of consistency and duplicate membership ids before exposing the collection.
- Follow existing public query result conventions for success, permission denial, missing client, validation failure and recalculation failure; failed results expose neither state nor actions.
- Keep PostgreSQL loading/cache integrity checks, DI registration, `GetClientProfile` composition, warning projection, Razor/htmx UI and visit allocation policy outside this contract-only step.

Scope:

- Add `GetClientMembershipStatesQuery`, `GetClientMembershipStatesResult` and `GetClientMembershipStatesStatus` as the public client-scoped collection query contract.
- Add controlled `IssuedMembershipLifecycleStatus` and immutable `ClientMembershipStateTimelineItem` metadata around the existing canonical state read model.
- Add `ClientMembershipStatesReadModel`, `ActiveMembershipCandidateSelection` and `ActiveMembershipCandidateStatus` for deterministic timeline plus explicit selection outcome.
- Add pure `ClientMembershipStatesPolicy.Create` for selector validation, canonical client/as-of consistency, duplicate rejection, deterministic ordering and ambiguity-preserving candidate classification.
- Carry client-scoped allowed actions on a successful collection result so the future handler/profile can expose the already established `memberships.issue` permission intent without client-side authorization logic.
- Add 14 focused core cases covering query shape, empty/single/expired/canceled/corrected/ambiguous outcomes, deterministic ordering and tie-break, defensive read-only storage, selector/state validation, stable result errors and permission projection.
- Add no query handler/DI registration, EF record/configuration/migration, PostgreSQL read, cache repair, profile DTO composition, controller/page or UI change.

Validation:

- Focused `ClientMembershipStatesQueryContractsTests` validation passed all 14 cases.
- Wider `FullyQualifiedName~Memberships` core regression passed all 134 tests.
- Solution formatting/analyzer verification passed without changes.
- The first EF drift command used unsupported `--no-connect` with the pinned `dotnet-ef 10.0.4` and exited before model comparison; the corrected `dotnet-ef migrations has-pending-model-changes` command built successfully and reported no model drift. This contract/domain-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 188 core tests, 35 web tests, 237 PostgreSQL infrastructure tests, 24 Playwright smoke tests and unchanged EF migration listing through `20260713194005_AddMembershipAdjustments`.
- `graphify update .` completed the structural rebuild with 4732 nodes, 9553 edges and 578 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): define client state collection`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Implement only the PostgreSQL `GetClientMembershipStates` query handler: canonical Admin/Owner authorization, required selector validation, client existence, no-tracking issued-membership/cache/extension reads, controlled lifecycle mapping, current-version/domain rehydration for every timeline row, Step 81 policy execution and client-scoped issue permission projection. Add focused PostgreSQL tests and scoped DI registration. Keep `GetClientProfile` composition, profile warning/status projection, search integration, Razor/htmx UI and multiple-active-membership product resolution outside that handler-only step.

## Step 82 - PostgreSQL client membership state collection handler

Status: completed.

Plan alignment:

- Continue Milestone 5 by connecting the Step 81 public client-scoped Memberships collection contract to canonical PostgreSQL reads before composing the membership section of `GetClientProfile`.
- Reuse the established Owner, named Admin and shared Reception/Admin account/session authorization path; forged, inactive, expired, ended, unknown and invalid actor/session shapes are denied before client or membership data is returned.
- Validate required client id and `as_of` date, distinguish a missing client from a valid client with no issued memberships, and return an empty successful collection with the established `memberships.issue` permission intent for the latter.
- Read every issued membership for the client with no tracking and a left join to `membership_state_cache`, so a missing derived row cannot silently remove source history from the timeline.
- Require every cache row to use the current recalculation version and rehydrate every snapshot/cache/extension combination through Memberships domain constructors. Missing, stale or inconsistent state fails the whole collection with `recalculation_failed`; queries never repair derived state on read.
- Map only the PostgreSQL-controlled `active`, `canceled` and `corrected` source lifecycle values into the Step 81 enum, then delegate ordering and `none`/`single`/`ambiguous` candidate classification to `ClientMembershipStatesPolicy`.
- Preserve all canceled/corrected/expired rows in the deterministic timeline while allowing only lifecycle-active and date-active rows to become candidates. Multiple candidates remain explicitly ambiguous with no arbitrary current membership.
- Extract the canonical persistence-to-domain state mapping into one infrastructure factory and reuse it from both direct `GetMembershipState` and client collection handlers, preventing snapshot/cache/extension projection drift.
- Keep query execution read-only: no cache mutation, audit entry, idempotency row, profile composition, search projection, controller/page or UI behavior.

Scope:

- Add `GetClientMembershipStatesQueryHandler` with canonical authorization, selector/client validation, no-tracking source/cache reads, batched extension explanation loading, lifecycle mapping, full-state integrity checks and Step 81 policy execution.
- Add internal `MembershipStateReadModelFactory` for current-version cache validation, immutable snapshot/domain rehydration and stable extension explanation ordering.
- Refactor `GetMembershipStateQueryHandler` to use the same factory without changing its public result or opening-state action semantics.
- Extend `MembershipQuerySupport` with controlled source lifecycle mapping and register the collection handler as scoped `IBodyLifeQueryHandler<GetClientMembershipStatesQuery, GetClientMembershipStatesResult>` through `AddBodyLifePersistence`.
- Add seven focused cases covering all accepted operational roles, empty collection/issue permission, canonical multi-row timeline and extension projection, active/canceled/corrected/expired classification, explicit ambiguity, missing/stale/inconsistent cache without read repair, stable selector/client failures, denied actor/session shapes, no query side effects and scoped DI registration.
- Add no EF record/configuration/migration, schema/index change, state mutation, `GetClientProfile` DTO/handler composition, search integration, page/controller or UI test.

Validation:

- Focused `PostgreSqlGetClientMembershipStatesQueryTests` validation passed all 7 cases against Docker PostgreSQL.
- Existing direct `PostgreSqlGetMembershipStateQueryTests` compatibility regression passed all 8 cases after sharing the rehydration factory.
- Wider `FullyQualifiedName~Membership` PostgreSQL regression passed all 137 tests.
- Wider `FullyQualifiedName~Memberships` core regression passed all 134 tests.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` built successfully and reported no model drift; this handler-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 188 core tests, 35 web tests, 244 PostgreSQL infrastructure tests, 24 Playwright smoke tests and unchanged EF migration listing through `20260713194005_AddMembershipAdjustments`.
- `graphify update .` completed the structural rebuild with 4784 nodes, 9727 edges and 571 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): handle client state collection`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Compose only the membership area of `GetClientProfile` through the public `GetClientMembershipStates` query: pass the canonical actor/client/`as_of`, project the deterministic timeline without formulas, expose a current summary only for a `single` candidate, surface an explicit server warning for `ambiguous`, and propagate permission/not-found/recalculation failures without partial optimistic profile state. Add focused core and PostgreSQL profile tests. Keep search-result membership summaries, issue-membership Razor/htmx UI, visits/payments/freezes/history sections and resolution of the multiple-active-membership product policy outside that profile-composition step.

## Step 83 - Canonical membership composition in client profile

Status: completed.

Plan alignment:

- Continue Milestone 5 by replacing the membership placeholder in `GetClientProfile` with composition through the Step 81/82 public `GetClientMembershipStates` query.
- Pass the canonical actor, client id and one resolved membership `as_of` date to Memberships. An omitted optional profile date resolves once from the server request time and the resolved date is returned with the profile; an explicitly empty date is rejected before composition.
- Keep Memberships as the only formula owner. The Clients projection copies the deterministic public timeline, canonical remaining visits, effective end date and warnings; its active/expired display status delegates to the Memberships-owned `IsActiveByDate` result and performs no date or visit arithmetic.
- Preserve source lifecycle history as stable `active`, `expired`, `canceled` or `corrected` summary status codes without dropping canceled/corrected/expired timeline rows.
- Expose `CurrentMembership` only when Memberships returns a `single` active candidate. A `none` result has no current summary, while `ambiguous` has no arbitrary current summary and carries the explicit `membership_current_ambiguous` server warning.
- For a single candidate, copy only that candidate's canonical Memberships warning codes/messages into the profile membership area. Do not reinterpret negative, zero, ending-soon, low-remaining or expired rules in Clients or Razor.
- Merge the Memberships-provided `memberships.issue` permission with the existing profile identity/card permissions so future UI remains driven by server authorization intent.
- Fail the whole profile composition on nested permission denial, missing client, invalid membership request or recalculation failure. No failure returns identity/card data as a partial optimistic profile.
- Keep the read path side-effect free: no audit entry, idempotency row, cache repair, source mutation or direct cross-module persistence write.
- Keep search-result membership summaries, issue-membership Razor/htmx interaction, visits/payments/freezes/history composition and resolution of the multiple-active-membership product policy outside this step.

Scope:

- Add `ClientProfileMembershipProjection` as a pure Clients-side adapter over the public Memberships collection, plus stable summary status and ambiguous-warning codes.
- Add `RecalculationFailed` to the client profile result contract with the stable `recalculation_failed` error and no profile payload.
- Inject the public client membership states query handler into `GetClientProfileQueryHandler`, resolve/validate `MembershipAsOfDate`, project successful canonical state, merge allowed actions and map every nested failure without partial data.
- Add five focused core cases for single/none/ambiguous projection, canonical warning/status copying, deterministic/read-only collections and recalculation failure shape.
- Extend PostgreSQL profile coverage to ten cases total, including empty profile issue permission, resolved default `as_of`, canonical single timeline, ambiguity, missing-cache failure, nested failure propagation and read-only/no-audit behavior.
- Add no EF record/configuration/migration, schema/index change, search query composition, page/controller/Razor/htmx code or new business formula.

Validation:

- Focused `ClientProfileMembershipProjectionTests` validation passed all 5 cases.
- Focused `PostgreSqlGetClientProfileQueryTests` validation passed all 10 cases against Docker PostgreSQL.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this composition-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 193 core tests, 35 web tests, 248 PostgreSQL infrastructure tests, 24 Playwright smoke tests and unchanged EF migration listing through `20260713194005_AddMembershipAdjustments`.
- `graphify update .` completed the structural rebuild with 4823 nodes, 9860 edges and 560 communities.
- `graphify . --update` was attempted for the progress documentation change but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(clients): compose canonical membership profile`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Close the remaining Milestone 5 ownership gate before starting Visits: add only an automated architecture check that formula-bearing Memberships types/rules are not referenced by production modules outside Memberships, while explicitly allowing public read/query contracts such as the profile projection. Pair it with a short Milestone 5 acceptance audit that records the still-unresolved multiple-active-membership/visit-allocation product decision. Keep the product-policy decision itself, search membership summaries, issue-membership Razor/htmx UI and all Milestone 6 persistence/commands outside that gate-only step.

## Step 84 - Membership formula ownership gate and Milestone 5 audit

Status: completed.

Plan alignment:

- Close only the remaining Milestone 5 architecture/review gate before any Visit implementation.
- Treat the Core `BodyLife.Crm.Modules.Memberships` namespace and Infrastructure `Persistence.Memberships` namespace as the formula owners.
- Permit production code outside those owners to depend only on an explicit allowlist of reviewed Memberships commands, queries, results, read models, warnings and action metadata.
- Inspect compiled type signatures, fields, locals and IL member/type tokens across Core, Infrastructure and Web so direct calls to calculators, rules, policies or newly exposed unreviewed Memberships types fail the test.
- Include a negative self-test that deliberately calls `MembershipDateRules` from an outside fixture, proving the inspector is active rather than vacuously green.
- Audit Milestone 5 without silently resolving or hiding its remaining closure gates: multiple active memberships/visit allocation, visit without an active membership, visit during an active freeze, the required pure Visit source-fact calculation tests, and adjustment participation in rebuild.
- Preserve the current ambiguity behavior: Memberships returns `ambiguous` with no selected membership, and no Visit schema/command may infer a candidate before the policy is accepted.
- Keep one-off negative closure explicitly deferred to Milestone 7 Payments; the current issue path continues to support only leaving prior negative state visible.

Scope:

- Add `MembershipFormulaOwnershipTests` and its reflection/IL dependency inspector to the infrastructure test assembly, which already references all three production assemblies.
- Use a reviewed contract allowlist rather than a package or source-text pattern, so a new Memberships type crossing the boundary requires an intentional test change.
- Add `docs/milestone-5-acceptance-review.md` with completed evidence, partial criteria, missing required-test coverage, unresolved product decisions and scope exclusions.
- Add no production code, NuGet package, EF record/configuration/migration, database schema/index, Visit/Payment/Freeze/NonWorkingDay workflow, page/controller or UI change.

Validation:

- The first focused attempt stopped at compile time because the inspector used the wrong named argument for `FieldInfo.GetValue`; the helper was corrected and no product behavior executed or failed.
- The next two focused iterations exposed reflection edge cases for non-generic constructors and non-catch exception clauses; the inspector was narrowed to valid runtime metadata shapes without weakening the ownership rule.
- Final focused `MembershipFormulaOwnershipTests` validation passed both cases: the production assemblies use only approved contracts and the deliberate outside formula call is rejected.
- Solution formatting/analyzer verification passed without changes.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this test/documentation-only step generated no migration.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 193 core tests, 35 web tests, 250 PostgreSQL/architecture infrastructure tests, 24 Playwright smoke tests and unchanged EF migration listing through `20260713194005_AddMembershipAdjustments`.
- `graphify update .` completed the structural rebuild with 4869 nodes, 9942 edges and 576 communities.
- `graphify . --update` was attempted for the acceptance/progress documentation changes but stopped because no semantic extraction LLM backend is configured.

Commits:

- `test(memberships): enforce formula ownership`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Before starting Milestone 6 persistence or commands, record an accepted product-policy decision for multiple active memberships and Visit allocation: whether multiples are allowed, how `MarkVisit` requires/selects a membership, what happens without an active membership, and how an active freeze affects marking. Preserve explicit selection/acknowledgement and the current no-arbitrary-candidate behavior. Keep Visit schema and implementation outside that decision-only step; use the following small implementation step to close the pure Memberships Visit-calculation test gap and explicitly resolve adjustment rebuild participation.

## Step 85 - Visit allocation and Freeze product policy

Status: completed.

Plan alignment:

- Complete only the product-decision gate identified by the Step 84 Milestone 5 audit; add no Visit schema, command contract implementation, persistence or UI.
- Accept ADR-014 so the decision becomes part of the governing architecture package rather than an informal implementation assumption.
- Permit multiple lifecycle-active issued Memberships because backdated/new-after-negative workflows must not silently retire or hide earlier source state.
- Keep the existing Memberships `none` / `single` / `ambiguous` boundary and forbid newest/first/best automatic selection under ambiguity.
- Require every membership `MarkVisit` command to carry an explicit `membership_id`; allow UI preselection only for one ordinary date-active candidate and require deliberate selection otherwise.
- Define Visit-date eligibility separately from display active-by-date: selected source status is active, Visit business date is not before start, expired selection requires current-state acknowledgement, and future-start selection is rejected.
- Define no-active behavior with no implicit default: Actor explicitly selects an expired Membership with all current warnings or chooses `one_off` / `trial` with no consumption or Memberships recalculation.
- Block membership Visit when an active inclusive Freeze covers the Visit business date. V1 has no override; correct/cancel Freeze first or use explicit one-off/trial without consuming the frozen Membership.
- Order active counted Visit facts by `occurred_at`, server `recorded_at`, then stable Visit id so first-negative identity/date and cancellation recalculation are deterministic.
- Preserve module ownership: Visits owns source facts; Memberships owns eligibility, warning requirements and all calculated state.

Scope:

- Add accepted `docs/adr/014-visit-membership-selection-and-freeze-policy.md` and register ADR-014 in the accepted package index.
- Synchronize the architecture baseline, domain model, data architecture, interaction contracts, UI workflows, implementation plan/roadmap, vertical-slice risk and Milestone 5 acceptance audit.
- Update `AGENTS.md` ADR source range from 001..013 to 001..014.
- Add stable design intent for `visit_during_freeze`, typed expired/zero/negative acknowledgements, membership vs one-off/trial consumption shape, locking/revalidation and required future tests.
- Add no C# source/test, NuGet package, EF model/migration, PostgreSQL table/index/constraint, Razor/htmx page or business workflow implementation.

Validation:

- `graphify query` was run first and identified `MarkVisit`, Visit, issued Membership, Freeze and the interaction contracts as the governing decision neighborhood.
- Cross-document consistency scans found and removed stale open-question/risk text in the interaction contracts, implementation plan/roadmap and vertical-slice plan; historical progress entries were intentionally preserved.
- `git diff --check` passed, the ADR-014 index target exists, and no ADR-001..013-only source range or unresolved Visit-allocation wording remains in current governing docs.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 193 core tests, 35 web tests, 250 PostgreSQL/architecture infrastructure tests, 24 Playwright smoke tests and unchanged EF migration listing through `20260713194005_AddMembershipAdjustments`.
- This documentation-only decision generated no EF model or migration change.
- `graphify . --update` was attempted for 13 changed project-knowledge documents but stopped because no semantic extraction LLM backend is configured; no code-only graph refresh was needed.

Commits:

- `docs(visits): define visit allocation policy`.

Next recommended step:

- Continue Milestone 5 with only a Memberships-owned pure Visit source-fact/eligibility calculation contract and focused domain tests for explicit selection, start/expiry eligibility, deterministic counted ordering, native vs honest opening-state baselines, signed remaining/negative state, known/unknown first-negative identity/date, cancellation exclusion and ADR-014 Freeze blocking inputs. Keep PostgreSQL Visit tables/commands, idempotency/audit orchestration, Razor/htmx UI and adjustment-rebuild handling outside that calculation-only step.

## Step 86 - Pure Memberships Visit eligibility and source-fact calculation

Status: completed.

Plan alignment:

- Close only the pure Visit-calculation test gap still recorded by the Milestone 5 acceptance review; do not start Milestone 6 persistence or commands.
- Keep one explicit selected Membership id at the boundary and reject Visit/Freeze inputs from another Membership instead of inferring or reallocating a candidate.
- Keep Visits as the future source owner while Memberships owns Visit-date eligibility, required warning acknowledgements, counted/remaining/negative formulas and first-negative identity/date.
- Treat lifecycle-active Memberships on or after `start_date` as selectable; expired selection remains eligible with typed acknowledgement, while future-start/canceled/corrected Memberships remain ineligible.
- Block a membership Visit on either inclusive endpoint of an active Freeze with stable `visit_during_freeze`; canceled Freeze sources do not block.
- Recalculate active counted Visit facts by `occurred_at`, server `recorded_at`, then stable Visit id; retain canceled facts in the input contract but exclude them from every calculated value.
- Start native history from the issue-time visit limit. Start incomplete history from the signed opening declaration and accept only facts not already represented in that baseline, without inventing historical Visits or first-negative metadata.
- Keep `membership_adjustments` rebuild participation as the next separate Milestone 5 closure step.

Scope:

- Add immutable `MembershipVisitSourceFact`/status and `MembershipVisitFreezeSource` inputs with explicit Membership ownership and source identity validation.
- Add `MembershipVisitEligibilityPolicy`, immutable result/status, stable error codes and typed expired/zero/negative acknowledgement requirements.
- Extend `MembershipStateCalculator` with native and opening-baseline Visit-fact entry points, deterministic ordering, signed state, first-negative transition, cancellation exclusion, last-counted Visit and representable-range checks.
- Add 17 focused domain cases across `MembershipVisitCalculationTests` and `MembershipVisitEligibilityPolicyTests`.
- Update the Milestone 5 acceptance review; add no EF record/configuration/migration, PostgreSQL Visit table/index/constraint, command handler, DI, idempotency, audit, Razor/htmx or UI change.

Validation:

- Focused `MembershipVisit*` validation passed all 17 new cases.
- Full core test validation passed all 210 cases.
- Focused `MembershipFormulaOwnershipTests` validation passed both cases, so the new formula-bearing types remain owned by Memberships.
- The first standalone format invocation used a nonexistent `.slnx` filename and stopped before analysis; rerunning against the repository's `BodyLife.Crm.sln` passed without changes.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 210 core tests, 35 web tests, 250 PostgreSQL/architecture infrastructure tests, 24 Playwright smoke tests and unchanged EF migration listing through `20260713194005_AddMembershipAdjustments`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this pure domain step generated no migration.
- The standard `graphify update .` attempt reached Python's unavailable forkserver and failed with `Errno 95`; the same full structural rebuild was rerun sequentially and completed with 4951 nodes, 10122 edges and 575 communities.
- `graphify . --update` was attempted for the acceptance/progress documentation changes but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): calculate visit eligibility and state`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Finish the last Milestone 5 closure gate by defining the controlled `membership_adjustments` calculation contract and making active visit/day adjustments participate in `MembershipStateCacheRebuilder` with focused domain and PostgreSQL rebuild/repair tests. Preserve inactive adjustment history, reject unsupported money semantics until Payments owns them, and add no Visit schema/commands or reception UI in that adjustment-only step.

## Step 87 - Controlled membership adjustment calculation and rebuild

Status: completed.

Plan alignment:

- Close only the final Milestone 5 technical gate recorded after Step 86; add no
  Visit schema, command, audit workflow or reception UI.
- Keep `membership_adjustments` as canonical retained source history and
  `membership_state_cache` as replaceable derived state.
- Accept only two active v1 calculation shapes: positive day-only
  `extension_days` and signed non-zero visit-only `visit_balance`.
- Make `visit_balance` adjust signed remaining/negative state without changing
  counted Visits or inventing first-negative Visit metadata.
- Make `extension_days` adjust the aggregate extension total and effective end
  without exposing a direct derived-state setter or fabricating calendar-day
  explanation rows.
- Preserve canceled/corrected rows while excluding them from current state;
  reject active money, unknown, mixed and negative-day semantics until their
  owning workflow defines them.
- Treat an active opening declaration as including facts recorded through its
  own `recorded_at`; apply only later adjustment records so backdated entries
  can participate without double-counting the declared baseline.
- Raise the cache recalculation version from 2 to 3 so older derived rows are
  detected and repaired under the adjustment-aware policy.

Scope:

- Add immutable `MembershipAdjustmentSourceFact`, source status and stable
  controlled type literals in Memberships.
- Extend `MembershipStateCalculator` with native and opening-baseline adjustment
  entry points, identity/shape validation, signed arithmetic and overflow
  protection.
- Extend `MembershipStateCacheRebuilder` to load retained adjustment facts under
  the issued-membership row lock, enforce the opening-state recording-time
  cutover, calculate version 3 state and roll back on unsupported active facts.
- Add 11 focused domain cases and four new PostgreSQL rebuild cases for native
  repair, inactive-history retention, opening cutover and rollback safety.
- Synchronize the domain model, data architecture and Milestone 5 acceptance
  review; accept Milestone 5 as complete for Milestone 6 handoff.
- Add no EF record/configuration/migration, adjustment writer/command,
  idempotency/audit orchestration, Visit table/handler, Razor/htmx or UI change.

Validation:

- Focused `MembershipAdjustmentCalculationTests` validation passed all 11 new
  domain cases.
- Focused `PostgreSqlMembershipStateCacheRebuildTests` validation passed all 16
  cases against Docker PostgreSQL, including the four new adjustment scenarios.
- Solution formatting/analyzer verification passed without changes.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 221 core tests, 35 web tests, 254 PostgreSQL/architecture infrastructure tests, 24 Playwright smoke tests and unchanged EF migration listing through `20260713194005_AddMembershipAdjustments`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this
  calculation/rebuild step generated no migration.
- `graphify update .` completed the structural rebuild with 4993 nodes, 10252
  edges and 576 communities.
- `graphify . --update` was attempted for the project-knowledge documentation
  changes but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): rebuild state from adjustments`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Start Milestone 6 with only canonical PostgreSQL source-fact storage for
  `visits`, `visit_consumptions` and `visit_cancellations`, including ADR-014
  membership/one-off/trial shape constraints, retained cancellation history,
  indexes and focused migration/storage tests. Keep `MarkVisit`/`CancelVisit`
  command orchestration, cache recalculation adapter, audit, reports and
  Razor/htmx UI outside that storage-only step.

## Step 88 - Visit source-fact PostgreSQL storage

Status: completed.

Plan alignment:

- Start Milestone 6 with only the canonical Visit/consumption/cancellation
  source-fact storage named by the roadmap; add no state-changing command or UI.
- Keep Visits as owner of arrival, explicit consumption and cancellation facts
  while Memberships remains the sole owner of calculated visit balance/state.
- Restrict Visit kind to ADR-014 `membership`, `one_off` or `trial` and retain
  active/canceled status history with actor, session, occurred/recorded time,
  entry origin, optional batch and comment/reason metadata.
- Repeat `client_id` and controlled `visit_kind` on consumption only as
  relational guards: composite FKs prove Visit/Membership Client equality and
  make consumption impossible for one-off/trial Visits.
- Keep initial Milestone 6 consumption semantics deliberately narrow:
  `counted`, sourced by the same Visit id, with active/canceled status. Future
  negative-closure/reallocation meanings require an explicit migration.
- Enforce at most one active counted consumption per Visit with a PostgreSQL
  partial unique index while allowing canceled historical rows to coexist.
- Keep one retained cancellation fact per Visit and use only restrictive FKs;
  no Visit, consumption or cancellation source history cascades away.

Scope:

- Add `VisitRecord`, `VisitConsumptionRecord` and `VisitCancellationRecord` plus
  their EF Core configurations under the Visits infrastructure boundary.
- Add composite alternate keys on Visits and issued Memberships, composite
  Visit/consumption and Membership/consumption FKs, controlled checks and
  report/recalculation/accountability indexes.
- Add migration `20260714140347_AddVisitsSourceFacts` and update the EF model
  snapshot.
- Add seven PostgreSQL integration cases for DDL shape, accountability/history,
  one-off/trial exclusion, cross-client rejection, active partial uniqueness,
  metadata/type checks and restrictive deletes.
- Synchronize `docs/data-architecture.md` with the implemented relational guard
  fields and their non-editable purpose.
- Add no `MarkVisit`/`CancelVisit` contract or handler, DI registration,
  idempotency, row locking, Memberships cache adapter, business audit, report
  query, Razor/htmx or UI change.

Validation:

- The first focused attempt stopped at compile time because a test helper
  declared `Task<long?>` while the PostgreSQL scalar helper returns
  `Task<long>`; the test-only signature was corrected before product behavior
  executed.
- Focused `PostgreSqlVisitsStorageTests` validation passed all 7 cases against
  Docker PostgreSQL.
- Generated SQL from `20260713194005_AddMembershipAdjustments` through
  `20260714140347_AddVisitsSourceFacts` was reviewed: it contains only the
  expected alternate key, three source tables, controlled checks, restrictive
  FKs, indexes and migration-history insert.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh` passed: Release build 0 warnings/errors, formatting/analyzers, 221 core tests, 35 web tests, 261 PostgreSQL/architecture infrastructure tests, 24 Playwright smoke tests and EF migration listing through `20260714140347_AddVisitsSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift.
- `graphify update .` completed the structural rebuild with 5067 nodes, 10432
  edges and 587 communities. `graph.json` and `GRAPH_REPORT.md` are current;
  graphify skipped the optional HTML visualization because the graph exceeded
  its default 5000-node visualization limit.
- `graphify . --update` was attempted for the project-knowledge documentation
  changes but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(visits): add visit source storage`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the public Visits `MarkVisit` command/result contract and pure
  preparation policy: distinguish membership from one-off/trial, require or
  reject `membership_id` accordingly, carry the common command envelope and
  typed expired/zero/negative acknowledgements, and delegate selected
  Membership eligibility to the existing Memberships boundary. Keep the
  PostgreSQL handler, idempotency claim, locks, source writes, recalculation,
  audit, DI and UI outside that contract-only step.

## Step 89 - MarkVisit public contract and preparation policy

Status: completed.

Plan alignment:

- Continue Milestone 6 with only the public `MarkVisit` boundary and pure
  preparation rules after canonical Visit storage; add no PostgreSQL command
  orchestration or reception UI.
- Carry the common command envelope, explicit Client, controlled
  `membership`/`one_off`/`trial` context, optional opening batch and typed
  current-state acknowledgements without copying occurred/actor/session/
  correlation/idempotency fields into a parallel Visit-specific envelope.
- Require a non-empty explicit `membership_id` only for membership Visits and
  reject any Membership selector, eligibility or acknowledgement for one-off
  and trial Visits.
- Consume only the immutable `MembershipVisitEligibility` result supplied by
  Memberships. Visits neither invokes `MembershipVisitEligibilityPolicy` nor
  receives remaining visits, negative balance, effective end date or other
  client-supplied formula state.
- Require the accepted acknowledgement set to exactly match Memberships'
  current expired/zero/negative requirements. Reject missing, stale extra,
  duplicate and unknown acknowledgement values.
- Preserve Memberships' stable ineligible/freeze reason for the later handler
  mapping and add the documented command errors
  `WarningAcknowledgementRequired` and `VisitDuringFreeze` without renumbering
  existing error values.
- Define canonical success as a new `visit` with a `client` reread target; a
  selected Membership may be returned as a related entity.

Scope:

- Add `VisitKind`, `MarkVisitCommand`, immutable `MarkVisitPreparation` and
  `MarkVisitPreparationPolicy` under the Visits module.
- Add only `MembershipVisitEligibility` and
  `MembershipVisitAcknowledgement` to the architecture-reviewed Memberships
  contracts available outside the owning module; formula implementations and
  calculated-state types remain forbidden.
- Add 17 focused command/preparation cases for envelope shape, canonical result
  targets, visit kinds, eligibility identity, Freeze rejection, exact
  acknowledgement matching, immutable preparation and invalid input guards.
- Add no EF record/configuration/migration, handler, authorization,
  idempotency claim, row lock, Visit/consumption write, Memberships
  recalculation, business audit, DI, report query, Razor/htmx or UI change.

Validation:

- Focused `MarkVisitCommandContractsTests` validation passed all 17 cases.
- Focused `MembershipFormulaOwnershipTests` validation passed both cases,
  proving Visits uses only the two explicitly reviewed Memberships contracts.
- Solution formatting/analyzer verification passed without changes.
- The first bare `./scripts/validate.sh` attempt stopped before restore because
  this shell has no system `dotnet` on `PATH`; the repository-local .NET 10
  invocation below then completed successfully.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet
  DOTNET_BIN=/tmp/bodylife-dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 238 core tests, 35 web tests, 261
  PostgreSQL/architecture infrastructure tests, 24 Playwright smoke tests and
  unchanged EF migration listing through
  `20260714140347_AddVisitsSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this contract-only step generated no migration.
- `graphify update .` completed the structural rebuild with 5110 nodes, 10534
  edges and 583 communities; the optional HTML visualization remains skipped
  above its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(visits): define mark visit contract`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only the Memberships-owned persistence adapter that projects retained
  canonical Visit/active-counted consumption rows into
  `MembershipVisitSourceFact` and includes them in
  `MembershipStateCacheRebuilder` under the existing issued-Membership lock.
  Cover deterministic ordering, canceled Visit/consumption exclusion, native
  rebuild and opening-state recorded-time cutover with focused PostgreSQL
  tests. Keep `MarkVisit` writes, idempotency, audit, DI and UI outside that
  recalculation-adapter step.

## Step 90 - Visit source projection into Memberships cache rebuild

Status: completed.

Plan alignment:

- Continue only the Milestone 6 recalculation prerequisite recommended after
  Step 89; add no `MarkVisit` command handler, Visit writer or reception UI.
- Keep Visits as owner of retained Visit/consumption source rows while the
  Memberships persistence boundary projects those rows into the existing
  immutable `MembershipVisitSourceFact` contract under the already-held issued
  Membership row lock.
- Collapse active and canceled consumption history for the same Visit and
  selected Membership into one effective source fact. Count it only when both
  the Visit and exactly one effective counted consumption are active.
- Use the effective consumption's server `recorded_at` for deterministic
  Memberships ordering and the active opening-state recording-time cutover;
  use Visit `occurred_at` for business date and last-counted chronology.
- Preserve the adjustment-aware rebuild introduced in Step 87. Apply supported
  active adjustment deltas to the native/opening baseline first, then apply
  ordered active Visit facts so adjustments do not become synthetic counted
  Visits or invent first-negative Visit metadata.
- Raise the rebuild calculation version from 3 to 4 so caches calculated before
  canonical Visit participation are detected and repaired.

Scope:

- Add combined native/opening Visit-plus-adjustment entry points to
  `MembershipStateCalculator` without changing the existing single-source
  contracts.
- Extend `MembershipStateCacheRebuilder` to join retained
  `visit_consumptions` to `visits`, collapse effective source state, apply the
  opening cutover and persist every stable Visit-derived cache field.
- Add three focused domain composition cases and three PostgreSQL rebuild cases
  for adjustment/Visit composition and ordering, canceled-history collapse,
  inactive source exclusion and effective-consumption opening cutover.
- Synchronize `docs/data-architecture.md` with the implemented effective
  consumption and recording-time semantics.
- Add no EF record/configuration/migration, Visit/consumption/cancellation
  writer, authorization, idempotency claim, business audit, DI registration,
  report query, Razor/htmx or UI change.

Validation:

- Focused `MembershipCombinedSourceCalculationTests` validation passed all 3
  new domain cases.
- Focused `PostgreSqlMembershipStateCacheRebuildTests` validation passed all 19
  cases against Docker PostgreSQL, including the three new Visit projection
  scenarios and every previous opening/adjustment/concurrency regression.
- Solution formatting/analyzer verification passed without changes.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet
  DOTNET_BIN=/tmp/bodylife-dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 241 core tests, 35 web tests, 264
  PostgreSQL/architecture infrastructure tests, 24 Playwright smoke tests and
  unchanged EF migration listing through
  `20260714140347_AddVisitsSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this query/calculation step generated no migration.
- `graphify update .` completed the structural rebuild with 5134 nodes, 10630
  edges and 593 communities; the optional HTML visualization remains skipped
  above its configured 5000-node limit.
- `graphify . --update` was attempted for the data-architecture/progress changes
  but stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): rebuild state from visit sources`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add only a Memberships-owned locked Visit eligibility preparation boundary:
  load the selected issued Membership and current cache under transaction,
  require explicit Freeze source input from the owning Freezes boundary, and
  rerun `MembershipVisitEligibilityPolicy` plus exact acknowledgement
  preparation against canonical state. Do not provide an unsafe empty-Freeze
  default, and keep Visit writes, idempotency, audit, DI and UI outside that
  read/preparation step.

## Step 91 - Locked Membership Visit eligibility preparation

Status: completed.

Plan alignment:

- Continue only the read/preparation prerequisite recorded after Step 90; add
  no Visit source write, command handler, idempotency claim, audit or UI.
- Require a caller-owned PostgreSQL transaction instead of opening and
  committing an internal transaction. This keeps the selected issued
  Membership row locked through the later Visit write/recalculation/audit
  consistency boundary and prevents a stale eligibility window.
- Lock by explicit `(membership_id, client_id)` and return the same typed
  `NotFound` shape for a missing Membership or wrong Client relationship.
- Rebuild the canonical version-4 Membership cache while the issued row remains
  locked, then evaluate Visit-date lifecycle/expiry/zero/negative/Freeze rules
  only through `MembershipVisitEligibilityPolicy`.
- Require a non-null explicit `MembershipVisitFreezeSource` collection. An
  explicit empty collection is valid input from the future Freezes boundary;
  no optional/default-empty product path exists.
- Return only the reviewed Memberships eligibility contract and rebuild status.
  Keep `MembershipCalculatedState` internal to the Memberships boundary so a
  future Visits handler cannot depend on formula-bearing implementation state.
- Compose the returned eligibility with Visits-owned
  `MarkVisitPreparationPolicy` for exact current acknowledgement matching,
  avoiding a Memberships-to-Visits production dependency or module cycle.

Scope:

- Add public `MembershipVisitEligibilityPreparer`, typed immutable result and
  `Prepared`/`NotFound` status under the Memberships persistence boundary.
- Reuse `MembershipStateCacheRebuilder` in the caller transaction and map the
  locked issue-time snapshot/lifecycle to the existing Memberships eligibility
  policy.
- Add five PostgreSQL cases for required Freeze input, required caller
  transaction, missing/wrong selection, canonical zero+expired warnings and
  Visits acknowledgement composition, inclusive active Freeze rejection and
  a real competing-update lock timeout until caller rollback.
- Add no EF record/configuration/migration, Freezes storage/provider,
  `MarkVisit` handler, Visit writer, authorization, idempotency, business audit,
  DI registration, report query, Razor/htmx or UI change.

Validation:

- Focused `PostgreSqlMembershipStateCacheRebuildTests` validation passed all 24
  cases against Docker PostgreSQL, including the five new eligibility/lock
  scenarios.
- Focused eligibility plus `MembershipFormulaOwnershipTests` validation passed
  all 26 selected cases, confirming the narrowed result does not expose a new
  formula dependency to Visits.
- During the API-narrowing review, the first focused compile attempted to read
  internal `MembershipStateCacheRecord` directly from the test assembly and
  failed with `CS0122`; that test-only access was removed rather than widening
  production visibility, then focused and full gates passed.
- Solution formatting/analyzer verification passed without changes.
- Final `CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet
  DOTNET_BIN=/tmp/bodylife-dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 241 core tests, 35 web tests, 269
  PostgreSQL/architecture infrastructure tests, 24 Playwright smoke tests and
  unchanged EF migration listing through
  `20260714140347_AddVisitsSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this transaction/read-boundary step generated no migration.
- `graphify update .` completed the structural rebuild with 5159 nodes, 10702
  edges and 591 communities; the optional HTML visualization remains skipped
  above its configured 5000-node limit.
- `graphify . --update` was attempted for the progress change but stopped
  because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): prepare locked visit eligibility`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Resolve the remaining ADR-014/Milestone ordering dependency by pulling
  forward only the minimal canonical Freeze source persistence and read query
  required by `MarkVisit`: retained active/canceled Freeze ranges and
  cancellation history plus an explicit Membership Visit source projection.
  Record this as a narrow Milestone 6 prerequisite to the later full Milestone
  8 Freeze workflow. Keep `AddFreeze`/`CancelFreeze`, extension recalculation,
  `MarkVisit` writes, DI and UI outside that storage/query-only step.

## Step 92 - Canonical Freeze source storage and locked Visit projection

Status: completed.

Plan alignment:

- Resolve only the ADR-014 dependency recorded after Step 91. This pulls the
  minimum Freeze source persistence/read boundary needed by Milestone 6 ahead
  of Milestone 8; it does not implement the later Freeze mutation workflow.
- Keep Freezes as owner of canonical range/cancellation facts and Memberships
  as owner of Visit eligibility and calculated state.
- Replace the caller-supplied Freeze collection on
  `MembershipVisitEligibilityPreparer` with a required
  `IMembershipVisitFreezeSourceProvider`. The preparer locks the selected
  `(membership_id, client_id)` first, then obtains the canonical Freeze
  projection, so the dependency cannot be accidentally defaulted to empty or
  read before the Membership lock.
- Make the PostgreSQL provider require the caller transaction and lock every
  inclusive-overlapping Freeze row with `FOR UPDATE`. Future Add/CancelFreeze
  commands must use the same Membership-first lock order.
- Keep `AddFreeze`, `CancelFreeze`, extension-day/state recalculation,
  idempotency, audit, command handlers, DI registration, history/report query,
  Razor/htmx and UI outside this step.

Scope:

- Add canonical `freezes` and `freeze_cancellations` EF records/configurations
  plus migration `20260714174210_AddFreezeSourceFacts`.
- Enforce inclusive `start_date <= end_date`, nonblank reasons, controlled
  entry origins/statuses, restrictive account/session/source relationships,
  one cancellation per Freeze and composite Membership/Client ownership.
- Add the planned `(membership_id, status, start_date, end_date)` recalculation
  and Visit lookup index plus client/cancellation timeline indexes.
- Retain active/canceled Freeze ranges and cancellation command-envelope
  metadata without hard deletion; canceled sources project with
  `IsActive = false` and therefore do not block a Membership Visit.
- Add a Freezes-owned `MembershipVisitFreezeSourceReader` implementing the
  reviewed Memberships provider contract and returning only overlapping
  canonical source ranges.
- Update the data architecture schema row and lock-order rule to match the
  implemented composite FK and cancellation envelope.

Validation:

- Release compile checks passed with 0 warnings/errors before and after
  migration/test work.
- Generated SQL from `20260714140347_AddVisitsSourceFacts` to
  `20260714174210_AddFreezeSourceFacts` was reviewed and contains only the two
  additive source tables, their restrictive relationships, checks and indexes.
- The first formatting verification found the UTF-8 BOM emitted by
  `dotnet-ef 10.0.4` in the new migration; the generated migration files were
  normalized to the repository's ASCII-compatible encoding and the repeated
  formatting/analyzer gate passed.
- Focused `PostgreSqlFreezesStorageTests`,
  `PostgreSqlMembershipStateCacheRebuildTests` and
  `MembershipFormulaOwnershipTests` validation passed all 31 cases against
  Docker PostgreSQL. The five new cases cover clean migration shape,
  active/canceled history, inclusive blocking, metadata/range/composite-FK
  constraints, unique retained cancellation, restrictive deletes, required
  caller transaction and a real competing Freeze update lock timeout.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 241 core tests, 35 web tests, 274
  PostgreSQL/architecture infrastructure tests, 24 Playwright smoke tests and
  EF migration listing through `20260714174210_AddFreezeSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift.
- `graphify update .` completed the structural rebuild with 5246 nodes, 10890
  edges and 598 communities; optional HTML visualization remained skipped
  above its configured 5000-node limit.
- `graphify . --update` was attempted for the architecture/progress changes but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(freezes): add locked visit source projection`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Continue Milestone 6 with the server-side `MarkVisit` command handler over
  the completed command, Visit storage, locked Membership eligibility and
  canonical Freeze projection prerequisites. Keep the next step bounded to
  authorization, idempotency, one PostgreSQL transaction, explicit
  membership/one-off/trial source writes, synchronous selected-Membership
  recalculation, `visit.marked` audit, rollback/concurrency tests and a
  canonical reread target; leave DI/web UI, cancellation and report/history
  presentation for following steps.

## Step 93 - Transactional MarkVisit command handler

Status: completed.

Plan alignment:

- Continue Milestone 6 exactly from the command/storage/eligibility prerequisites
  completed in Steps 89-92; implement only the server-side `MarkVisit` command
  transaction and keep DI, Razor/htmx presentation, `CancelVisit` and reports or
  history outside this step.
- Authorize only canonical active Owner/Admin account-session pairs and preserve
  the accepted named/shared Admin account kinds without pretending a shared
  account identifies a physical person.
- Require a validated command envelope and idempotency key, lock the Client and
  explicitly selected Membership/Freeze sources, and revalidate all current
  eligibility warnings inside one PostgreSQL transaction.
- Write an active `visits` source for `membership`, `one_off` and `trial`, but
  write an active counted `visit_consumptions` source and synchronously
  recalculate only for an explicit membership Visit.
- Keep Memberships as the sole owner of state formulas through a reviewed
  `IMembershipStateRecalculator` port; Visits receives only a typed completion
  result and the existing public Membership state query/read model.
- Append `visit.marked` business audit plus succeeded idempotency only after the
  source write and recalculation succeed, return the Client as canonical reread
  target, and roll the entire workflow back on any failure.

Scope:

- Add `MarkVisitCommandHandler`, shared Visits command validation/idempotency
  support and stable public `VisitAuditActions` identifiers.
- Normalize and fingerprint actor, origin, occurred-at, reason/comment,
  membership selection, acknowledgements and fallback batch context; reject
  mismatched same-key payloads while replaying the original completed result.
- Persist explicit membership consumption accountability, preserve separate
  occurred/recorded timestamps and fallback metadata, and include Visit plus
  before/after Membership summaries and accepted acknowledgements in audit.
- Add a narrow Memberships recalculation port and PostgreSQL adapter around the
  existing cache rebuilder; no formula-bearing cache type crosses the module
  boundary.
- Map unique idempotency races and PostgreSQL serialization/deadlock/lock timeout
  failures to stable duplicate/concurrency command errors and clear tracked
  state after rollback.
- Add ten PostgreSQL command cases covering atomic membership success, exact
  simultaneous expired/zero acknowledgements and negative transition,
  one-off/trial isolation, replay and changed payload, concurrent same-key
  serialization, missing selection, active Freeze rejection, paper fallback
  metadata, canonical authorization/envelope validation, competing row lock and
  audit-failure rollback.
- Add no EF record/configuration/migration, DI registration, web endpoint,
  Razor/htmx UI, Visit cancellation or report/history presentation.

Validation:

- Initial focused Release compile exposed only test visibility/nullability helper
  issues; `VisitAuditActions` was aligned with the repository's other public
  stable audit-action catalogs and the repeated compile passed with 0
  warnings/errors.
- The first focused PostgreSQL run passed 8 of 9 cases and exposed a real error
  mapping defect: EF wrapped `lock_not_available` in `InvalidOperationException`,
  which the eligibility catch incorrectly mapped to `recalculation_failed`.
  PostgreSQL-backed exceptions now pass through to the concurrency mapper; the
  repeated suite passed all 9 cases, and the added concurrent same-key case
  brought the final focused result to 10/10.
- Focused `PostgreSqlMembershipStateCacheRebuildTests`,
  `MembershipFormulaOwnershipTests` and `PostgreSqlMarkVisitCommandTests`
  validation passed all 35 cases. The ownership allowlist contains only the
  reviewed eligibility status and recalculation contracts, not Memberships
  formula implementations.
- Solution formatting/analyzer verification and `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 241 core tests, 35 web tests, 284
  PostgreSQL/architecture infrastructure tests, 24 Playwright smoke tests and
  unchanged EF migration listing through
  `20260714174210_AddFreezeSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this
  command-only step generated no migration.
- `graphify update .` completed the structural rebuild with 5366 nodes, 11258
  edges and 600 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress change but stopped because
  no semantic extraction LLM backend is configured.

Commits:

- `feat(visits): handle mark visit commands`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Continue Milestone 6 with only the application composition and server-rendered
  reception integration for the completed `MarkVisit` handler: register the
  reviewed handler/recalculation/query dependencies, expose a CSRF-protected
  profile action that supplies server-derived explicit Membership choices and
  acknowledgements, and refresh canonical Client/Membership fragments after
  success. Add tablet/phone Playwright coverage for membership, one-off/trial,
  warnings, busy/disabled duplicate-submit protection and stable command errors;
  keep `CancelVisit` and report/history presentation for later steps.

## Step 94 - Canonical MarkVisit options and application composition

Status: completed.

Plan alignment:

- Continue only the first server-side prerequisite of the Step 93 recommendation.
  The existing Client profile projection intentionally summarizes Membership
  state and cannot safely derive expired/zero/negative acknowledgements,
  future-start eligibility, ambiguous explicit choices or Freeze blocking in
  Razor.
- Add a canonical `GetMarkVisitOptions` query before building the profile form,
  so the next UI step consumes server-owned choices instead of duplicating
  Memberships formulas or reading persistence records.
- Keep the options response advisory and read-only. The `MarkVisit` command still
  reloads and locks the selected Membership plus overlapping Freeze sources and
  revalidates every rule in its transaction.
- Compose the completed query, command, recalculation, eligibility and Freeze
  services in DI, but keep Razor/htmx, CSS/JavaScript, Playwright workflow
  additions, `CancelVisit`, reports and history presentation outside this step.

Scope:

- Add typed Visits contracts for `GetMarkVisitOptions`, stable query statuses,
  `VisitActionKeys.Mark`, explicit Membership option rows and a sole-candidate
  suggestion that is cleared when the candidate is blocked by a Freeze.
- Return every lifecycle-active Membership as an explicit option with immutable
  issue-time type name, start/effective-end dates, signed remaining visits,
  server-derived warnings, eligibility status and exact typed acknowledgements.
  Canceled/corrected Memberships are not selectable options; future-start and
  active-Freeze rows remain visible but disabled; expired/zero/negative rows
  remain selectable only with their current acknowledgement set.
- Add a Memberships-owned eligibility evaluator boundary over canonical
  `MembershipStateReadModel`. The overload reconstructs and validates canonical
  issue/calculated state inside Memberships and requires the read model's as-of
  date to equal the Visit date.
- Add a separate read-only Freeze snapshot provider interface. The concrete
  Freezes reader implements both the non-locking query snapshot and the existing
  transaction-required `FOR UPDATE` command provider without weakening the
  command contract.
- Register `GetMarkVisitOptionsQueryHandler`, `MarkVisitCommandHandler`, the
  shared Freeze reader, Membership eligibility evaluator/recalculation ports and
  locked eligibility preparer in `AddBodyLifePersistence`.
- Add one domain regression and six PostgreSQL/registration cases covering a
  suggested single candidate, no-Membership one-off/trial context, exact
  expired+zero then expired+negative acknowledgements, ambiguous/future/frozen
  choices, canceled source exclusion, stable invalid/denied/missing/stale-cache
  failures and complete scoped DI resolution.
- Add no EF record/configuration/migration and no state-changing query behavior.

Validation:

- The first solution compile exposed an interface-segregation issue: adding the
  snapshot read to `IMembershipVisitFreezeSourceProvider` forced locked command
  test providers to implement an unrelated concern. The read path was moved to
  `IMembershipVisitFreezeSourceSnapshotProvider`; the original command interface
  remained byte-for-byte unchanged and the repeated Release build passed.
- Focused `MembershipVisitEligibilityPolicyTests` passed all 10 cases.
- Focused `PostgreSqlGetMarkVisitOptionsQueryTests`,
  `MembershipFormulaOwnershipTests`, `PostgreSqlFreezesStorageTests` and
  `PostgreSqlMembershipStateCacheRebuildTests` passed all 36 cases; the final
  options-only rerun passed all 6 query/DI cases.
- Solution formatting/analyzer verification and `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 242 core tests, 35 web tests, 290
  PostgreSQL/architecture infrastructure tests, 24 Playwright smoke tests and
  unchanged EF migration listing through
  `20260714174210_AddFreezeSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this
  query/composition step generated no migration.
- `graphify update .` completed the structural rebuild with 5443 nodes, 11455
  edges and 616 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress change but stopped because
  no semantic extraction LLM backend is configured.

Commits:

- `feat(visits): expose canonical mark visit options`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Continue Milestone 6 with the Razor/htmx Mark Visit profile action over the
  completed options query and command registration. Build a CSRF-protected form
  with explicit membership/one-off/trial selection, server-provided
  acknowledgements, retained editable context on errors, idempotency key and
  busy/disabled duplicate-submit protection; after success reread and replace
  the canonical reception workspace/profile. Add tablet and phone Playwright
  cases for ordinary membership, zero-to-negative acknowledgement, explicit
  one-off/trial, no duplicate source rows and stable blocked/stale errors. Keep
  `CancelVisit` and report/history presentation for following steps.

## Step 95 - Razor/htmx Mark Visit reception action

Status: completed.

Plan alignment:

- Continue Milestone 6 from the completed `MarkVisit` command and canonical
  options query with only the reception profile action. Keep `CancelVisit`,
  Visit history/report presentation, Payments and new persistence schema outside
  this step.
- Render explicit membership, one-off and trial choices from
  `GetMarkVisitOptions`; Razor and JavaScript display server-owned state and do
  not calculate eligibility, remaining visits, warnings or acknowledgement
  requirements.
- Submit every mutation through the registered `MarkVisitCommand`, preserve the
  command idempotency key and antiforgery token, and replace the full canonical
  reception workspace after success or state-sensitive failure.
- Keep the ordinary UI on the server-provided current UTC Visit timestamp.
  Backdated/manual/paper-fallback entry UX remains part of the later operational
  workflow rather than an unmarked date override in this quick action.

Scope:

- Add a typed `MarkVisitFormViewModel` and server-rendered profile action with a
  stable htmx target, explicit Visit kind, explicit Membership candidates,
  immutable snapshot details, eligibility state, server warning messages,
  exact expired/zero/negative acknowledgements and an optional comment.
- Compose options into both full-page and htmx profile reads. The sole ordinary
  Membership candidate may start selected; ambiguous, expired/no-suggestion and
  no-Membership contexts require a deliberate choice, while future-start and
  active-Freeze candidates stay visible but disabled.
- Add a CSRF-protected Razor Page POST that creates the common command envelope,
  invokes `MarkVisit`, verifies its canonical Client reread target and replaces
  `#reception-workspace` after success. Non-htmx submissions retain the normal
  redirect fallback.
- Requery current options on every command error. Validation errors replace only
  the action form; warning, eligibility, duplicate, permission, recalculation
  and concurrency errors reread the full profile while retaining editable Visit
  kind, Membership, comment and idempotency context. Submitted acknowledgements
  are retained only when they exactly equal the refreshed server requirements.
- Extend the shared busy-form JavaScript with Mark Visit selection
  synchronization. Membership acknowledgements are enabled and required only
  for the currently selected server-selectable Membership; one-off/trial clears
  stale Membership inputs; `hx-sync="this:drop"`, disabled/busy submit and
  command idempotency jointly protect fast repeated taps.
- Add responsive, restrained action styles with stable controls, candidate rows,
  warning blocks and phone stacking; no business formula is present in CSS,
  JavaScript or Razor.
- Extend the PostgreSQL UI fixture with issued Membership snapshots, canonical
  cache rebuilds and evidence reads for Visits, consumptions, audit and
  idempotency. Add six Playwright cases covering ordinary tablet/phone
  membership Visits, CSRF token presence, canonical profile reread,
  zero-to-negative acknowledgement, repeat tap while busy, explicit one-off and
  trial without consumption, changed-warning refresh and active-Freeze blocking
  with an explicit one-off fallback.
- Add no EF record/configuration/migration and no product workflow that writes
  directly to PostgreSQL; direct source inserts exist only in isolated UI test
  setup to simulate concurrent canonical-state changes.

Validation:

- The first Release solution build after the Razor/htmx implementation passed
  with 0 warnings/errors; the repeated fixture and final builds also passed.
- The first focused Playwright run passed 5 of 6 cases and exposed only a test
  locator issue: the busy button correctly changed its accessible name from
  `Mark visit` to `Marking...`. The helper now uses its stable data selector.
- The next run exposed two test timing/selector issues: a post-login
  `NetworkIdle` race and two identical negative-warning messages in canonical
  profile and action option state. The helper now waits for the Reception
  heading and scopes the assertion to the membership panel. The final focused
  `MarkVisitSmokeTests` run passed all 6 cases.
- The full UI smoke project passed all 30 tests, including every pre-existing
  reception, membership catalog and staff-account scenario.
- `dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore` and
  `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 242 core tests, 35 web tests, 290
  PostgreSQL/architecture infrastructure tests, 30 Playwright smoke tests and
  unchanged EF migration listing through
  `20260714174210_AddFreezeSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift; this
  UI/test step generated no migration.
- Optional Playwright screenshot capture reran both ordinary viewport cases
  successfully. Original-resolution tablet and phone form/success images showed
  no overlap, clipped text or horizontal overflow.
- `graphify update .` completed the structural rebuild with 5520 nodes, 11727
  edges and 622 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress change but stopped because
  no semantic extraction LLM backend is configured.

Commits:

- `feat(ui): add Mark Visit reception action`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Continue Milestone 6 with a small server-side `CancelVisit` prerequisite:
  define the public command/result preparation contracts and a locked canonical
  Visit-plus-active-consumption cancellation projection over the existing
  `visits`, `visit_consumptions` and `visit_cancellations` schema. Cover
  ownership, already-canceled/not-found, reason/comment, idempotency and
  changed-after-close placeholders without implementing the transaction handler,
  profile history UI or reports in the same step.

## Step 96 - CancelVisit contract and locked source preparation

Status: completed.

Plan alignment:

- Continue the next unfinished Milestone 6 task after the complete `MarkVisit`
  reception path. This step prepares cancellation input and canonical source
  state only; it does not claim that cancellation mutations are implemented.
- Keep Visits as owner of Visit, consumption and cancellation source facts.
  The public command accepts only the Visit id plus the common command envelope
  and optional batch reference; Client and Membership ownership come from the
  locked canonical PostgreSQL projection rather than caller input.
- Require an explicit normalized reason, allow a separate optional comment,
  require a bounded idempotency key and preserve occurred-at/entry-origin/batch
  metadata for the later cancellation fact and audit entry.
- Preserve the existing server-side changed-after-close result contract as a
  required preparation input. The future handler must derive it from canonical
  day reconciliation state when that optional source exists; this step adds no
  client-controlled day-close flag or reconciliation table.
- Make source preparation require a caller-owned PostgreSQL transaction and
  lock the Visit first, then its active consumption and retained cancellation.
  This is the lock order the future mutation handler must reuse.
- Keep authorization, canonical session revalidation, idempotency claim/replay,
  cancellation/status writes, Memberships recalculation, business audit, DI,
  profile/history UI and Reports outside this bounded prerequisite.

Scope:

- Add `CancelVisitCommand`, immutable `VisitCancellationSource`,
  `CancelVisitPreparation` and `CancelVisitPreparationPolicy` under the Visits
  public module boundary.
- Define cancellation success entity names for the new cancellation fact,
  original Visit relation and canonical Client reread target. Add the documented
  `ReasonRequired` error at the end of `CommandErrorCode` without renumbering
  existing values.
- Normalize reason, comment, idempotency key and cancellation occurred time;
  reject missing/oversized metadata, empty ids, invalid entry origin, normal
  entry with a backfill/fallback batch, mismatched Visit identity, canceled
  source and invalid membership/non-membership consumption shapes.
- Add `CancelVisitSourcePreparer` and typed prepared/not-found/already-canceled/
  inconsistent result statuses. It projects retained Visit metadata and only
  the active counted consumption, recognizes either canceled Visit status or an
  existing cancellation as `AlreadyCanceled`, and never writes source state.
- Lock `visits`, active `visit_consumptions` and existing
  `visit_cancellations` with PostgreSQL `FOR UPDATE` in a caller transaction.
  Existing composite foreign keys remain the ownership guarantee; the projected
  Client, Membership and consumption ids are checked again before preparation.
- Add 19 focused core contract/policy cases and five PostgreSQL preparation
  cases to the existing seven Visit storage cases. Cover canonical result and
  changed-after-close placeholders, reason/comment/idempotency validation,
  membership and one-off/trial shapes, ownership, not-found, already-canceled,
  inconsistent source and competing updates blocked on all three source paths.
- Add no EF record/configuration/migration and no Razor, JavaScript, CSS or
  report/history query change.

Validation:

- Focused `CancelVisitCommandContractsTests` passed all 19 cases.
- The first focused PostgreSQL build found one missing test namespace import for
  `EntryOrigin`; no product code executed. After adding the import, focused
  `PostgreSqlVisitsStorageTests` passed all 12 cases, including real lock timeout
  evidence for Visit, active consumption and cancellation rows.
- `dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore` and
  `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 295
  PostgreSQL/architecture infrastructure tests, 30 Playwright smoke tests and
  unchanged EF migration listing through
  `20260714174210_AddFreezeSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this contract/read-only persistence step generated no migration.
- `graphify update .` completed the structural rebuild with 5587 nodes, 11909
  edges and 611 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(visits): prepare CancelVisit sources`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Implement only the transactional `CancelVisit` command handler over the
  completed contract and locked source preparation: canonical Owner/Admin
  authorization with the existing after-close policy placeholder, idempotency
  claim/replay, one cancellation fact, Visit/consumption status updates,
  synchronous affected-Membership recalculation, `visit.canceled` audit,
  rollback/concurrency mapping and canonical Client reread. Keep DI/Razor
  cancellation UI, profile history rows and report-source presentation for
  following small steps.

## Step 97 - Transactional CancelVisit handler

Status: completed.

Plan alignment:

- Continue the unfinished Milestone 6 `CancelVisit` workflow directly after its
  public contract and locked source preparation. This step implements the
  command transaction only and does not broaden into UI, history or Reports.
- Reuse the Step 96 PostgreSQL lock order and canonical projection instead of
  accepting Client, Membership or consumption ownership from the caller.
- Keep Memberships as the sole owner of counted/remaining/negative Visit state.
  The handler synchronously rebuilds and rereads Membership state before and
  after the cancellation inside the same transaction; Visits does not copy any
  membership formula.
- Preserve the accepted correction contract: one retained cancellation fact,
  canceled source statuses, explicit reason/comment, command idempotency,
  append-only `visit.canceled` audit and canonical Client reread.
- Add a typed day-reconciliation status boundary without inventing a day-close
  table. Open days permit canonical Owner/Admin actors; a reconciled day
  requires Owner and marks both the result and audit as changed after close.
- Keep concrete day-status wiring, DI registration, Razor/htmx cancellation UI,
  profile history rows and report-source presentation for later bounded steps.

Scope:

- Add `IVisitDayReconciliationStatusProvider` and
  `VisitDayReconciliationStatus` to the Visits public module boundary. The
  provider is queried with the original Visit business date; unsupported status
  values fail closed instead of silently assuming an open day.
- Extend `VisitCommandSupport` with cancellation-specific normalization,
  fingerprinting, successful replay, idempotency record creation, PostgreSQL
  error mapping and canonical success/error result construction.
- Implement `CancelVisitCommandHandler` with canonical account/session
  authorization, caller envelope validation, pre-lock and post-lock idempotency
  checks, locked source preparation and reconciled-day Owner authorization.
- For Membership Visits, rebuild and reread canonical state before mutation,
  cancel exactly one active Visit and its active counted consumption, insert one
  retained `visit_cancellations` fact, then rebuild and reread state after the
  mutation. One-off/trial cancellation writes no consumption or Membership
  cache state.
- Append `visit.canceled` audit against the original Visit with source metadata,
  cancellation fact, before/after Membership summary and changed-after-close
  marker; persist the successful command replay record and commit everything as
  one `ReadCommitted` PostgreSQL transaction.
- Map missing/already-canceled/inconsistent sources, duplicate payloads,
  permission/day-close failures, lock/deadlock/serialization failures and
  recalculation failures to the shared command taxonomy. Unexpected persistence
  failures roll back and clear tracked state before they propagate.
- Add eight PostgreSQL command scenarios to the existing twelve Visit storage
  and source-preparation cases. Cover membership and one-off success, retained
  metadata, canonical reread, first-negative Visit movement, replay, changed
  payload rejection, different-key already-canceled behavior, concurrent
  same-key serialization, reason/not-found/inactive-actor errors,
  reconciled-day Admin denial and Owner marker, Visit lock conflict, failed
  second recalculation and audit-write rollback.
- Add no EF record/configuration/migration and no DI, Razor, JavaScript, CSS,
  profile-history or report change.

Validation:

- The first local build attempt found no Linux `dotnet` on `PATH`; the pinned
  repository SDK was already installed at `/home/genik/.dotnet`. Using that
  .NET SDK 10.0.301, the focused Infrastructure test-project build passed with
  0 warnings/errors.
- Focused `PostgreSqlVisitsStorageTests` passed all 20 cases against the healthy
  Docker PostgreSQL service, including all eight new command scenarios.
- `dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore` and
  `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 303
  PostgreSQL/architecture infrastructure tests, 30 Playwright smoke tests and
  unchanged EF migration listing through
  `20260714174210_AddFreezeSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this transaction-handler step generated no migration.
- `graphify update .` completed the structural rebuild with 5650 nodes, 12166
  edges and 592 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(visits): implement transactional CancelVisit`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add the next small Milestone 6 read-side prerequisite for cancellation UI:
  expose a Visits-owned canonical Client Visit row query containing active and
  canceled source rows plus retained cancellation metadata and allowed actions.
  Register only the handler/query dependencies needed by that boundary and
  cover PostgreSQL ownership/status ordering. Keep the Razor cancel form,
  Playwright mutation flow and report-source projection for following steps.

## Step 98 - Canonical Client Visit rows query

Status: completed.

Plan alignment:

- Continue the next unfinished Milestone 6 read-side prerequisite after the
  transactional `CancelVisit` handler. This step exposes Visits-owned source
  rows needed by profile/history composition without implementing the Razor
  correction form or report query.
- Keep `GetClientProfile` as the future composition boundary while Visits owns
  the canonical Visit, consumption and cancellation projection. The query does
  not calculate Membership state or reinterpret report totals.
- Preserve retained history: active and canceled Visits remain queryable;
  membership Visits include their counted consumption and issue-time Membership
  type name snapshot; canceled Visits require their explicit retained
  cancellation fact.
- Return server-authoritative row actions. Open-day active Visits expose the
  Admin/Owner cancel action, reconciled-day active Visits expose Owner-only
  permission or an explicit Admin denial, and canceled rows expose no cancel
  action.
- Since a day reconciliation workflow/table is still outside current Milestone
  6 scope, register an explicit open-day provider behind the public status
  boundary. A future reconciliation implementation can replace that provider
  without changing the query or command contracts.
- Keep profile composition/rendering, the Razor/htmx cancel form, Playwright
  cancellation mutation and Reports-owned daily source projection for later
  bounded steps.

Scope:

- Add `GetClientVisitRowsQuery`, result/status types and a bounded page contract
  with default limit 20, maximum limit 100 and `HasMore` indication.
- Add immutable public Visit row, counted-consumption and cancellation models.
  Rows retain Visit actor/session, occurred/recorded times, kind, entry origin,
  batch/comment, active/canceled status, Membership id/type snapshot and full
  cancellation reason/actor/session/origin metadata.
- Extend `VisitActionKeys` with `visits.cancel` and the accepted Owner-only
  policy name alongside the existing Admin/Owner policy.
- Implement `GetClientVisitRowsQueryHandler` with canonical account/session
  authorization, Client existence and limit validation, Client ownership
  filtering and deterministic `occurred_at DESC, recorded_at DESC, id DESC`
  ordering.
- Load selected Visit, consumption/Membership snapshot and cancellation source
  rows without tracking. Fail closed on duplicate/missing/mismatched source
  shapes, unknown enum-like values, canceled Visit without retained
  cancellation, membership Visit without exactly one counted consumption or
  Visit/consumption lifecycle disagreement.
- Cache day status once per distinct active Visit business date and attach a
  row-level `QueryPermissionSet`; reads create no business audit entry.
- Add `VisitQuerySupport` for module-local actor authorization, source mapping
  and cancel permission construction, plus a stateless
  `OpenVisitDayReconciliationStatusProvider` implementation.
- Register the new query, day-status provider and locked source preparer, and
  make the already validated transactional `CancelVisit` command handler
  resolvable through the application DI boundary.
- Add six focused tests in a separate PostgreSQL fixture: owned active/canceled
  projection and retained metadata, no read audit, deterministic tie ordering
  and paging, Owner/Admin reconciled-day actions, empty/invalid/not-found/
  inactive-actor responses, inconsistent retained source rejection and full DI
  resolution of the query plus `CancelVisit` command.
- Add no EF record/configuration/migration and no Razor, JavaScript, CSS,
  profile model, Playwright flow or report-source change.

Validation:

- The first narrow build found that a primary-constructor default could not
  reference a constant declared in the record body; using the same literal
  default fixed the contract without behavior change. The next build found two
  short-circuit `out` assignments in source mapping; explicit initialization
  fixed the compile-only issue.
- The first test build found only an assertion against the intentionally
  internal open-day implementation type. The test now verifies interface
  lifetime and resolved behavior without widening the production API.
- The final focused Infrastructure project build passed with 0 warnings/errors,
  and focused `PostgreSqlGetClientVisitRowsQueryTests` passed all 6 cases
  against Docker PostgreSQL.
- `dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore` and
  `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 309
  PostgreSQL/architecture infrastructure tests, 30 Playwright smoke tests and
  unchanged EF migration listing through
  `20260714174210_AddFreezeSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this read-only query/DI step generated no migration.
- `graphify update .` completed the structural rebuild with 5741 nodes, 12382
  edges and 626 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(visits): query canonical Client Visit rows`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Compose `GetClientVisitRowsQuery` into the canonical Client profile and render
  a compact read-only recent Visits section for tablet and phone. Show active vs
  canceled state, membership/one-off/trial context, occurred/recorded and
  backfill/fallback labels, cancellation reason and server permission state.
  Keep the state-changing Razor cancel form and report-source projection for
  subsequent steps.

## Step 99 - Recent Visit history in Client profile

Status: completed.

Plan alignment:

- Continue the next unfinished Milestone 6 profile/history task after the
  Visits-owned canonical row query. This step composes and renders Visit rows;
  it does not add a second Visit projection or move Membership formulas into
  Clients/Search or Razor.
- Keep `GetClientProfile` as the server composition boundary and request recent
  Visit history only for visible reception profiles. Identity/card-only helper
  rereads remain lightweight.
- Preserve active and canceled history with server-owned cancellation
  permissions. The UI displays source status and metadata but does not infer
  whether cancellation is allowed.
- Keep the state-changing Cancel Visit form/handler endpoint and report-facing
  daily Visit projection for subsequent bounded Milestone 6 steps.

Scope:

- Extend `ClientProfile` with an optional `ClientVisitRowsPage`. When
  `IncludeHistory` is true, `GetClientProfileQueryHandler` composes the existing
  `GetClientVisitRowsQuery`; without it, no Visit query is required and the
  profile contract keeps `RecentVisits` null.
- Fail the profile atomically when Visit history is denied, missing, invalid or
  source-inconsistent. Add a stable profile `source_inconsistent` result rather
  than returning identity/Membership data beside untrusted Visit rows.
- Request `IncludeHistory` from the reception profile and canonical workspace
  paths, including the post-MarkVisit reread. The card-conflict helper query
  remains history-free because it does not render a profile.
- Render an unframed recent-Visits section after the primary Mark Visit action.
  Rows show Membership snapshot or one-off/trial context, active/canceled text
  status, occurred/recorded UTC times, non-normal entry-origin labels, retained
  comment, cancellation reason/time/source and the server-provided cancellation
  permission state.
- Add responsive list/meta styles for the established tablet two-area and phone
  one-column layouts. Status color is paired with text, long content wraps and
  no business state is calculated in CSS, Razor or browser state.
- Add PostgreSQL profile-composition coverage for requested Visit rows and all
  mapped query failures. Existing profile requests assert history remains
  opt-in.
- Extend the existing tablet/phone Mark Visit Playwright theory: verify the
  active history row, cancel that exact row through the real transactional
  `CancelVisitCommandHandler`, reread by exact card search, verify retained
  canceled history and reason, then prove active Visit/consumption counts and
  rebuilt Membership state agree.
- Add no EF record/configuration/migration, public cancellation endpoint,
  report query or JavaScript change.

Validation:

- Focused `PostgreSqlGetClientProfileQueryTests` passed all 12 cases against
  Docker PostgreSQL after a compile-only nullable assertion was tightened.
- The Web project and UI smoke project each built in Release with 0
  warnings/errors.
- Focused tablet/phone Visit history Playwright coverage passed 2/2. A second
  run with full-page screenshots also passed 2/2; visual review at 1024x768 and
  390x844 confirmed readable active/canceled rows, retained cancellation detail
  and no overlap or horizontal overflow.
- The first full validation attempt passed build, 261 core, 35 web and 311
  infrastructure tests, then one unrelated Staff Account smoke timed out while
  waiting for login `NetworkIdle`. Its isolated rerun passed 3/3 in 7 seconds.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 311
  PostgreSQL/architecture infrastructure tests, 30 Playwright smoke tests and
  unchanged EF migration listing through
  `20260714174210_AddFreezeSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this profile/read-only UI step generated no migration.
- `git diff --check` passed.
- `graphify update .` completed the structural rebuild with 5755 nodes, 12449
  edges and 615 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(visits): render recent Visit history`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add the bounded state-changing Cancel Visit correction form to each eligible
  active profile-history row. Require explicit confirmation plus reason/comment,
  use a fresh idempotency key and busy/duplicate-submit protection, execute the
  existing `CancelVisit` command, then reread the canonical Client profile.
  Cover open-day Admin/Owner permission states, failure rendering and tablet/
  phone Playwright mutation flow. Keep the daily report source projection for
  the following step.

## Step 100 - Cancel Visit correction from Client profile

Status: completed.

Plan alignment:

- Continue the next unfinished Milestone 6 correction workflow after canonical
  Visit history became visible in the reception profile. This step exposes the
  already validated `CancelVisit` command through Razor; it does not duplicate
  cancellation rules or mutate Visit/Membership state in the PageModel.
- Render the state-changing form only for an active row whose server-provided
  permission set contains `visits.cancel`. Owner/Admin open-day behavior and
  Owner-only reconciled-day behavior remain owned by the query and command.
- Require explicit confirmation and a reason, retain business history, create
  audit/idempotency facts through the command, and always reread canonical
  workspace state after a completed or source-sensitive attempt.
- Keep the daily report-facing Visit source/query and report consistency tests
  for the next bounded Milestone 6 step.

Scope:

- Add `CancelVisitFormViewModel` and input models with a fresh idempotency key,
  retained search context, bounded reason/comment fields and stable local error
  mappings. Canonical-refresh forms preserve entered text but clear confirmation
  and rotate the key supplied by the reread row.
- Add `OnPostCancelVisitAsync` to the reception PageModel. The server verifies
  confirmation before dispatch, builds the standard command envelope, invokes
  the existing transactional `CancelVisit` handler and validates the primary
  cancellation plus canonical Client/Visit reread identities before rendering
  success.
- Return field-level validation and reason failures to the inline form. Refresh
  the full canonical workspace for stale, permission, duplicate, not-found,
  already-canceled, day-close and recalculation outcomes so browser state is
  never treated as business truth.
- Compose one correction form for each eligible active Visit row and render it
  inline beneath that row. The form includes retained Visit context, correction
  warning, required confirmation/reason, optional comment, antiforgery token,
  idempotency key, htmx busy/disabled state and duplicate-submit drop behavior.
- Add scoped responsive styling for the established tablet two-area and phone
  one-column profile layouts. Canceled rows remain read-only retained history.
- Replace direct test-fixture cancellation in the Visit history smoke with the
  real Owner UI workflow. Assert successful command execution, canonical
  canceled history, restored Membership state, one audit entry and one
  idempotency record on tablet and phone.
- Add a named-Admin browser case that bypasses HTML confirmation validation,
  proves the server rejects the attempt without mutation/audit/idempotency,
  then confirms and successfully retries the same open-day correction.
- Add no EF record/configuration/migration, JavaScript bundle, report query or
  daily report UI change.

Validation:

- The first focused Playwright attempt could not connect because Docker Desktop
  was stopped. After it started, Windows reported local port `55432` inside a
  dynamically excluded range, so the unchanged Compose service was run on
  temporary host port `55532` for this validation only.
- The first browser-capable focused run passed 5/7 cases; the two viewport cases
  found an ambiguous exact-text locator because the Membership snapshot appears
  both as the Visit heading and form context. Scoping the assertion to the
  heading fixed the test without changing product behavior.
- Final focused `MarkVisitSmokeTests` passed all 7 cases against Docker
  PostgreSQL. A separate screenshot run passed 2/2; visual review at 1024x768
  and 390x844 confirmed readable correction forms and canceled history with no
  overlap or horizontal overflow.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 311
  PostgreSQL/architecture infrastructure tests, 31 Playwright smoke tests and
  unchanged EF migration listing through
  `20260714174210_AddFreezeSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this command/UI step generated no migration.
- `graphify update .` completed the structural rebuild with 5779 nodes, 12546
  edges and 616 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(visits): add Cancel Visit reception flow`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add the report-facing daily Visit source/query shape and PostgreSQL
  consistency tests. Read canonical active/canceled Visit facts and Memberships
  public state without reimplementing Membership formulas, preserve correction
  visibility, and prove daily totals equal drill-down source rows. Keep report
  Razor composition for a subsequent bounded step.

## Step 101 - Daily Visit report source readiness

Status: completed. Milestone 6 is complete.

Plan alignment:

- Complete the final unfinished Milestone 6 task: prepare the report-facing
  query/source shape for daily Visit totals and cancellations. The public read
  boundary remains owned by Visits so a later Reports query can compose it with
  Payments and Memberships without querying Visit tables independently.
- Count every active canonical Visit kind (`membership`, `one_off`, `trial`) for
  the selected business date, exclude canceled Visits from the total, and keep
  active plus canceled source rows in one explainable drill-down.
- Derive the active total from the exact returned source rows so total and
  drill-down filters cannot diverge. Reuse the same fail-closed source mapper as
  Client profile history rather than reinterpreting Visit/consumption status.
- Do not implement Milestone 9 `GenerateDailyReport`, payment totals, audit
  timeline composition, report Razor/htmx UI or Membership formulas in this
  bounded source-readiness step.

Scope:

- Add `GetDailyVisitSourceRowsQuery`, result/status types and
  `DailyVisitSourceSnapshot`/`DailyVisitSourceRow` contracts. The snapshot
  exposes business date, day reconciliation status, canonical rows and an
  active count derived from those rows.
- Implement `GetDailyVisitSourceRowsQueryHandler` over one half-open UTC
  business-date range. It joins current Client display identity for navigation,
  orders rows deterministically by occurred time, recorded time and Visit id,
  and preserves actor/session, kind, origin/batch, comment, Membership snapshot
  consumption and full cancellation metadata.
- Enforce canonical active Owner/named Admin/shared Reception Admin session
  authorization. Attach server-owned open/reconciled-day cancellation
  permissions to active rows and return no action for canceled history.
- Move existing Visit/consumption/cancellation source-shape validation into one
  shared Visits mapper used by both Client profile history and the daily source
  query. Unknown/missing/mismatched status, consumption or retained
  cancellation fails the complete read with `source_inconsistent`.
- Register the query handler through the application query DI boundary. Reads
  remain no-tracking and create no business audit entry.
- Add `ix_visits_daily_source` on `(occurred_at, status, client_id)` while
  retaining the smaller partial active-total index. Add EF migration
  `20260715211212_AddDailyVisitSourceIndex` and review its idempotent SQL.
- Add six focused tests covering all Visit kinds across Clients, exact
  total/drill-down equality, retained cancellation and backfill metadata, UTC
  range boundaries, deterministic ordering, empty/invalid/denied results,
  reconciled Owner/Admin permissions, fail-closed source inconsistency, DI and
  the PostgreSQL query plan. Extend the Visit storage migration test with the
  new index definition.

Validation:

- The Infrastructure project and test project built in Release with 0
  warnings/errors. Focused `PostgreSqlGetDailyVisitSourceRowsQueryTests` passed
  all 6 cases, the migration/index case passed 1/1 and the existing shared
  profile mapper regression suite passed 6/6 against Docker PostgreSQL.
- The first idempotent migration SQL command used `--no-build` immediately
  after generation and therefore read the previous compiled migration assembly.
  Repeating with a build succeeded and produced only guarded creation of
  `ix_visits_daily_source` plus the migration history row.
- The first full validation attempt stopped before tests because EF generated
  the two migration files with a UTF-8 BOM while the repository formatter
  requires UTF-8 without BOM. Removing those generated BOM markers made the
  standalone formatting gate pass.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 317
  PostgreSQL/architecture infrastructure tests, 31 Playwright smoke tests and
  EF migration listing through
  `20260715211212_AddDailyVisitSourceIndex`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift.
- `graphify update .` completed the structural rebuild with 5851 nodes, 12730
  edges and 613 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(visits): add daily Visit report source`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Start Milestone 7 with canonical cash Payment source storage: define the
  minimal `payments`, `payment_cancellations` and `payment_corrections` records,
  controlled v1 cash/context/status values, correction-preserving relationships,
  daily-report indexes, one reviewable EF migration and PostgreSQL
  constraint/index tests. Keep Create/Correct Payment commands and UI for later
  bounded steps.

## Step 102 - Canonical Payment source storage

Status: completed. Milestone 7 is in progress.

Plan alignment:

- Complete the first bounded Milestone 7 task from the roadmap by introducing
  PostgreSQL source facts for cash Payments and explicit cancellation/replacement
  history before implementing commands or UI.
- Keep daily cash truth canonical: only `active` cash rows contribute to totals,
  while `canceled` and `replaced` originals remain queryable and correction or
  cancellation facts explain the transition.
- Enforce the roadmap's `amount > 0`, cash-only method, controlled context,
  entry-origin and same-client Membership relationship rules in PostgreSQL.
- Do not implement `CreatePayment`, `CorrectPayment`, issue-with-payment
  integration, negative-closure policy, report query composition, Audit hooks or
  Razor/htmx flows in this storage-only step.

Scope:

- Add EF records and configurations for `payments`, `payment_cancellations` and
  `payment_corrections`. Payment facts retain client, optional Membership,
  amount/currency, cash method, controlled context, occurred/recorded times,
  account/session, origin/batch, comment and canonical status.
- Accept only `membership_sale`, `one_off`, `trial`, `negative_closure` and
  `other` contexts; retain `active`, `canceled` and `replaced` Payment statuses.
  Currency stays a canonical uppercase value consistent with existing
  Membership price storage rather than introducing an unaccepted accounting
  or multi-method model.
- Add the composite nullable FK
  `FK_payments_issued_memberships_membership_client`: a standalone Payment may
  omit Membership, but a supplied Membership must belong to the Payment client.
- Preserve replacement explainability with separate original and replacement
  Payment rows, a non-empty JSON array of changed fields, required reason,
  actor/session/origin metadata, distinct-row checks and same-client composite
  FKs. Unique original/replacement indexes keep each replacement relationship
  unambiguous while still allowing a later correction chain.
- Preserve cancellation history with one reasoned cancellation fact per
  Payment and restrictive foreign keys; application commands will own the
  future cross-table terminal-state transaction.
- Add covering partial/full daily-cash indexes, client and Membership timeline
  indexes, correction/cancellation timelines and accountability indexes.
- Add EF migration `20260715213519_AddPaymentSourceFacts`; remove the UTF-8 BOM
  emitted in its two generated files to satisfy the repository formatter.
- Add four PostgreSQL tests covering exact tables/checks/indexes, replacement
  history and active daily totals, optional/cross-client Membership behavior,
  invalid amount/method/context/currency/origin/status/comment values, unique
  correction/cancellation relationships and restrictive history deletes.

Validation:

- The first migration-generation attempt used a stale Debug assembly because
  it combined Release-only prior build output with `--no-build`. That empty
  migration was removed before application; regeneration with an explicit
  Release build produced the intended three-table migration.
- Focused `PostgreSqlPaymentsStorageTests` passed all 4 cases against Docker
  PostgreSQL, including clean migration apply and canonical daily cash totals
  before/after replacement.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 321
  PostgreSQL/architecture infrastructure tests, 31 Playwright smoke tests and
  EF migration listing through
  `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift.
  Idempotent migration SQL from `20260715211212_AddDailyVisitSourceIndex` was
  reviewed and contains only the three guarded Payment tables, their explicit
  constraints/indexes and the migration-history row.
- `graphify update .` completed the structural rebuild with 5922 nodes, 12886
  edges and 629 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(payments): add canonical Payment source storage`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add the standalone `CreatePayment` command boundary and PostgreSQL handler:
  active Owner/Admin session authorization, cash/context validation, optional
  same-client Membership link, idempotency, one transaction, `payment.created`
  business audit and a canonical Payment result. Prove ordinary standalone
  Payments do not alter Membership negative state. Keep issue-with-payment,
  `CorrectPayment`, daily-cash query/UI and negative-closure policy for later
  bounded steps.

## Step 103 - Standalone Create Payment workflow

Status: completed. Milestone 7 is in progress.

Plan alignment:

- Complete the next bounded Milestone 7 task from the roadmap by adding the
  standalone `CreatePayment` public command and PostgreSQL handler over the
  canonical Payment source storage introduced in Step 102.
- Require a canonical active Owner, named Admin or shared Reception/Admin
  account and session, validate cash amount/context and optional same-client
  Membership linkage, and preserve command accountability metadata.
- Execute Payment creation, `payment.created` business audit and successful
  idempotency storage in one transaction. Return the Payment as primary result,
  the Client as canonical reread target and the Membership as a related entity
  when supplied.
- Keep ordinary Payment entry independent from Membership recalculation and
  prove it cannot hide an existing negative Membership state. Reserve the
  `negative_closure` context in the public enum but reject it in this standalone
  workflow until its explicit policy and command orchestration are implemented.
- Do not implement issue-with-payment integration, `CorrectPayment`, Payment
  history/report queries, daily-cash UI or negative-closure behavior in this
  bounded command step.

Scope:

- Add `CreatePaymentCommand` and `PaymentContext` public contracts, including a
  stable `payment` primary result kind and `client` canonical reread target.
- Add shared Payment command support for actor/session authorization,
  validation and normalization, deterministic request fingerprints,
  idempotent replay, PostgreSQL duplicate/concurrency mapping and transaction
  cleanup.
- Implement `CreatePaymentCommandHandler` with `ReadCommitted`, a serialized
  Client lock, an optional same-client Membership key-share lock, one active
  cash source fact, append-only audit and a 24-hour successful idempotency row.
- Register the handler through the application command DI boundary.
- Add ten PostgreSQL tests covering all allowed actor and context shapes,
  backfill metadata, replay and changed-payload duplicates, concurrent same-key
  serialization, invalid/reserved inputs, missing and cross-client records,
  ended sessions, competing Membership locks, audit-triggered rollback/tracker
  cleanup, DI registration and unchanged negative Membership state.
- Reuse the existing Payment tables and indexes; this command step changes no
  EF model and generates no migration.

Validation:

- The Infrastructure project built in Release with 0 warnings/errors, and the
  standalone formatting/diff checks passed.
- The first PostgreSQL test attempt reached the DI-only case but the remaining
  cases could not connect to `localhost:55532` because Docker Desktop had
  stopped. Docker Desktop startup then exposed stale Windows runtime sockets;
  removing those sockets and restarting the engine restored the existing
  `bodylife-crm-postgres` container without changing repository files.
- Focused `PostgreSqlCreatePaymentCommandTests` then passed all 10 cases against
  Docker PostgreSQL.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 331
  PostgreSQL/architecture infrastructure tests, 31 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this command step generated no migration.
- `graphify update .` completed the structural rebuild with 6024 nodes, 13207
  edges and 646 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(payments): add standalone CreatePayment workflow`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add the Payments-owned canonical Client payment-history query/read model.
  Preserve active, canceled and replaced source rows together with
  correction/cancellation metadata, expose no mutation permissions yet, and
  add PostgreSQL fail-closed consistency tests. Keep `CorrectPayment`, the
  reception payment form and Reports-owned daily-cash composition for later
  bounded steps.

## Step 104 - Canonical Client Payment rows query

Status: completed. Milestone 7 is in progress.

Plan alignment:

- Continue the next bounded Milestone 7 read-side prerequisite after the
  transactional standalone `CreatePayment` command. This step exposes
  Payments-owned source rows needed by Client profile/history composition
  without implementing a second projection in Clients/Search or Razor.
- Preserve canonical history: active, canceled and replaced Payment rows remain
  visible; a canceled row requires its retained cancellation fact, while a
  replaced row requires its outgoing correction and links both original and
  replacement sides to the same correction metadata.
- Keep payment facts typed and explainable with cash method, context, Money,
  optional issue-time Membership type snapshot, actor/session,
  occurred/recorded times, entry origin/batch, comment, reason and structured
  changed fields.
- Expose no correction/cancellation actions yet because `CorrectPayment` and
  day-close policy are not implemented. The read boundary does not infer future
  permissions, calculate Membership state or reinterpret daily cash totals.
- Keep profile composition/rendering, `CorrectPayment`, reception mutation
  forms and Reports-owned daily-cash source composition for later bounded
  steps.

Scope:

- Add `GetClientPaymentRowsQuery`, result/status types and a bounded page
  contract with default limit 20, maximum limit 100 and `HasMore` indication.
- Add immutable public Payment row, method/status, cancellation and correction
  models. Correction links are explicit in both directions so a replacement
  chain remains understandable even when its counterpart falls outside the
  selected page.
- Implement `GetClientPaymentRowsQueryHandler` with canonical active Owner,
  named Admin or shared Reception/Admin session authorization, Client
  existence and limit validation, Client ownership filtering and deterministic
  `occurred_at DESC, recorded_at DESC, id DESC` ordering.
- Read Payment, optional Membership snapshot, cancellation and correction
  records without tracking. Parse `changed_fields` with `System.Text.Json` and
  fail closed on non-string/empty/duplicate field names, unknown enum-like
  values, noncanonical cash/currency/Membership relationships, duplicate
  retained facts or lifecycle/status disagreement.
- Register the query through the application query DI boundary. Reads create no
  business audit entry and expose no `QueryPermissionSet` until the mutation
  policy exists.
- Add six focused tests covering owned active/canceled/replaced rows, two-sided
  correction links and cancellation/backfill metadata, no read audit,
  deterministic ordering/paging, empty/invalid/not-found/inactive-actor
  responses, missing retained cancellation, malformed correction JSON and DI.
- Reuse the existing Payment timeline indexes; this read-only step changes no
  EF model and generates no migration.

Validation:

- The Infrastructure and Infrastructure test projects built in Release with 0
  warnings/errors. Focused `PostgreSqlGetClientPaymentRowsQueryTests` passed all
  6 cases against Docker PostgreSQL.
- An exploratory `dotnet format` run elevated all existing repository IDE
  suggestions to `info` and therefore returned exit 2; the standard repository
  `dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore` gate passed,
  as did `git diff --check`.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 337
  PostgreSQL/architecture infrastructure tests, 31 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this query/DI step generated no migration.
- `graphify update .` completed the structural rebuild with 6108 nodes, 13412
  edges and 638 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(payments): query canonical Client Payment rows`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Compose `GetClientPaymentRowsQuery` into the canonical Client profile and
  render a compact read-only recent Payments section for tablet and phone.
  Show active/canceled/replaced state, amount/context, occurred/recorded and
  backfill/fallback labels, retained cancellation reason and two-sided
  correction explanation. Keep add/correct Payment forms, mutation permissions
  and Reports-owned daily-cash composition for subsequent bounded steps.

## Step 105 - Recent Payment history in Client profile

Status: completed. Milestone 7 is in progress.

Plan alignment:

- Continue the next bounded Milestone 7 profile/history task after the
  Payments-owned canonical Client row query. This step composes and renders
  Payment rows without adding a second Payment projection or moving source
  interpretation into Clients/Search or Razor.
- Keep `GetClientProfile` as the server composition boundary and request recent
  Payment history only when `IncludeHistory` is true. Existing visible
  reception profile paths already request history, while helper/card-conflict
  rereads remain lightweight.
- Preserve active, canceled and replaced history with cash/context, source
  labels, cancellation reason and both incoming/outgoing correction
  explanations. Razor formats trusted typed read models and performs no daily
  cash or Membership calculations.
- Expose no Payment mutation action because `CorrectPayment`, day-close policy
  and the reception Add Payment form are separate unfinished workflows.
- Keep `CreatePayment` form handling, issue-with-payment integration,
  correction commands/forms and Reports-owned daily-cash composition for later
  bounded steps.

Scope:

- Extend `ClientProfile` with optional `ClientPaymentRowsPage`. When history is
  requested, `GetClientProfileQueryHandler` sequentially composes the existing
  Visits and Payments queries on the scoped DbContext.
- Map Payment query permission/not-found/validation/source failures to stable
  profile results and reject mismatched Client pages. The profile fails
  atomically instead of returning identity/Membership/Visit data beside
  untrusted Payment history.
- Render an unframed Recent payments section after Recent visits. Rows show
  amount/currency, cash method, context, optional Membership snapshot,
  active/canceled/replaced text status, occurred/recorded UTC times,
  non-normal source labels, comment, retained cancellation details and
  two-sided correction reason/changed-fields metadata.
- Add semantic replaced/canceled/correction colors paired with visible text,
  wrapping metadata grids and a one-column phone layout. The section contains
  no buttons or inferred mutation permission.
- Extend the PostgreSQL profile-composition suite with canonical Payment page
  composition, captured query defaults, every mapped failure and mismatched
  page ownership. Existing profile reads verify Payment history remains opt-in.
- Seed a real four-row Payment history in the UI smoke database: active trial,
  canceled one-off with retained cancellation, active correction replacement
  and replaced original with source/correction metadata.
- Extend the existing exact-card tablet/phone Playwright path to verify all
  statuses, amounts, Membership snapshot, source labels, correction directions,
  no mutation buttons and no horizontal overflow.
- Add no EF record/configuration/migration and no Payment mutation endpoint,
  form model or JavaScript change.

Validation:

- The Infrastructure test project built in Release with 0 warnings/errors, and
  focused `PostgreSqlGetClientProfileQueryTests` passed all 13 cases against
  Docker PostgreSQL.
- The Web and UI smoke projects built in Release with 0 warnings/errors.
  Focused `ReceptionSearchAndProfileReadPathWorksOnTargetViewport` Playwright
  coverage passed 2/2 against the real app and PostgreSQL at 1024x768 and
  390x844.
- Full-page tablet and phone Payment-history screenshots were inspected. Both
  preserve readable source/correction content, status labels and one-column
  phone flow without overlap or horizontal overflow.
- `dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore` and
  `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 338
  PostgreSQL/architecture infrastructure tests, 31 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this profile/UI step generated no migration.
- `graphify update .` completed the structural rebuild with 6116 nodes, 13448
  edges and 630 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(payments): show recent Payments in Client profile`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add the reception Add Payment Razor/htmx workflow over the existing
  `CreatePaymentCommand`: expose a server-authoritative profile action, accept
  positive cash amount, supported non-negative-closure context, optional
  same-client Membership and occurred time/comment, prevent duplicate submit
  with idempotency and reread the canonical profile on success. Keep
  issue-with-payment integration, `CorrectPayment` and daily-cash Reports for
  subsequent bounded steps.

## Step 106 - Reception Add Payment workflow

Status: completed. Milestone 7 is in progress.

Plan alignment:

- Continue the next bounded Milestone 7 task after canonical Payment source,
  command, Client row query and profile history. This step exposes the existing
  `CreatePaymentCommand` through the reception profile without creating a
  second mutation path or moving Payment validation into Razor.
- Keep this profile action to normal same-day standalone cash Payments in UAH.
  Supported contexts are membership sale, one-off, trial and other;
  `negative_closure` remains blocked by the command and is not offered by the
  UI until its explicit workflow is accepted and implemented.
- Preserve the roadmap warning that a standalone Payment does not close
  negative visits or recalculate Membership state. The profile rereads
  canonical state after success instead of applying optimistic Payment or
  Membership values.
- Keep manual backfill/paper fallback entry, issue-with-payment transaction
  integration, `CorrectPayment` and Reports-owned daily cash composition for
  subsequent bounded steps.

Scope:

- Add the Payments-owned `PaymentActionKeys.Create` permission and compose it
  into `GetClientProfile`. The profile remains server-authoritative for whether
  the Add Payment form is exposed, while `CreatePaymentCommandHandler` repeats
  canonical account/session, Client and Membership authorization in its
  PostgreSQL transaction.
- Extend the existing profile Membership timeline summary with its immutable
  issue-time type-name snapshot so the optional same-Client Membership choice
  is recognizable without an extra UI-owned projection.
- Add `AddPaymentFormViewModel`, input model and `_AddPaymentForm` Razor partial.
  The form fixes method to cash and currency to UAH, accepts a positive amount,
  one supported context, optional canonical Membership, current UTC occurred
  time and optional comment, and carries the current search context plus a new
  idempotency key.
- Validate adapter-only form shape before constructing `Money`: required
  positive amount/context/time, non-empty optional Membership id, normal
  current UTC date and no future occurred time. Domain and persistence
  validation remain in `CreatePaymentCommand` and PostgreSQL constraints.
- Submit through an antiforgery-protected Razor handler with `hx-sync` drop,
  disabled/busy button state and command idempotency. Verify the returned
  Payment id and canonical Client reread target before reporting success.
- Return validation failures in the open action panel after rebuilding its
  choices from a lightweight canonical profile query. Non-validation failures
  refresh the entire workspace; successful commands reread full profile
  history and display the new canonical Payment row and audit reference.
- Show a visible warning that standalone Payment does not close negative visits
  or recalculate Membership state. Do not expose negative closure, correction,
  cancellation or report actions in this step.
- Add isolated tablet and phone fixtures plus PostgreSQL evidence helpers. The
  tablet path creates a Membership-linked membership-sale Payment; the phone
  path creates a standalone one-off Payment. Both first prove zero-amount
  rejection without source/audit/idempotency rows, then prove busy duplicate
  tap protection, exactly one active Payment/audit/idempotency row, canonical
  profile history and unchanged Membership state where linked.

Validation:

- The Web, Infrastructure test and UI smoke projects built in Release with 0
  warnings/errors. Focused `ClientProfileMembershipProjectionTests` passed 5/5,
  `PostgreSqlGetClientProfileQueryTests` passed 13/13 against Docker PostgreSQL,
  and focused `AddPaymentSmokeTests` passed 2/2 at 1024x768 and 390x844.
- Full-page tablet and phone form/success screenshots were inspected. The
  normal-payment warning, amount/context/Membership inputs, validation error,
  touch submit and canonical Payment row remain readable without overlap or
  horizontal overflow; phone controls collapse to one column.
- `dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore` and
  `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 338
  PostgreSQL/architecture infrastructure tests, 33 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this permission/profile/Razor workflow generated no migration.
- `graphify update .` completed the structural rebuild with 6179 nodes, 13638
  edges and 634 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(payments): add reception Payment workflow`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Implement optional cash Payment inside `IssueMembership` as one atomic
  workflow: extend the issue command/preview/form contract, commit issued
  Membership, Payment, initial recalculation, audit and idempotency together,
  and prove rollback on Payment/recalculation/audit failure. Do not compose it
  by calling the standalone profile Payment form after issuance. Keep
  `CorrectPayment` and daily-cash Reports for later bounded steps.

## Step 107 - Atomic IssueMembership Payment integration

Status: completed. Milestone 7 is in progress.

Plan alignment:

- Complete the roadmap's bounded Milestone 7 command/persistence portion of
  issue-with-payment after standalone Payment source facts and reception entry
  exist. The optional cash Payment is part of `IssueMembership`; it is not a
  follow-up call to the standalone Add Payment form or command.
- Preserve module ownership through a Payments-owned
  `IMembershipIssuePaymentWriter` port. Memberships coordinates one transaction,
  while Payments owns staging the canonical `payments` row and
  `payment.created` audit event.
- Keep accepted v1 semantics narrow: payment method is cash, the integrated
  context is `membership_sale`, amount must be positive and currency remains a
  canonical `Money` value. Payment presence does not hide an existing negative
  Membership; the existing explicit negative-decision rules still apply.
- Keep the reception issue form, preview/form composition, `CorrectPayment`
  and Reports-owned daily cash work for later bounded steps.

Scope:

- Extend `IssueMembershipCommand` with optional `MembershipIssuePayment`
  amount/context input and include its canonical amount, currency and context
  in the IssueMembership idempotency fingerprint.
- Validate positive amount, canonical currency, membership-sale context and the
  Payment row's comment limit before opening or mutating business state.
- Add `MembershipIssuePaymentWriter` in Payments. Inside the caller-owned
  transaction it stages one active cash Payment linked to the new Membership,
  copies actor/session/origin/occurred/recorded metadata and appends the
  separate `payment.created` business audit.
- Coordinate Payment staging after initial Membership state rebuild and before
  final save/commit. The existing single IssueMembership idempotency record is
  retained; no nested `CreatePayment` transaction or second command key is
  created.
- Include Payment id and summary plus its Payment audit id in the
  `membership.issued` audit, while the Payment audit points back to Client and
  Membership. Command success still directs the UI to reread the canonical
  Client profile.
- Add command-contract and PostgreSQL integration evidence for optional/no
  Payment behavior, source metadata, both audits, payment-sensitive
  fingerprint replay, concurrent same-key submission and rollback on
  recalculation, Payment persistence, Payment audit and Membership audit
  failures.
- Reuse the existing Payment schema and migration; add no EF record,
  configuration or migration.

Validation:

- Focused `IssueMembershipCommandContractsTests` passed 12/12.
- Focused PostgreSQL `PostgreSqlIssueMembershipCommandTests` passed 17/17
  against Docker PostgreSQL, including atomic success, idempotent/concurrent
  replay and rollback failure injection.
- `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 341
  PostgreSQL/architecture infrastructure tests, 33 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` built successfully and
  reported no model drift; this transaction-composition step generated no
  migration.
- `graphify update .` completed the structural rebuild with 6202 nodes, 13725
  edges and 653 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): issue memberships with optional payment`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add the reception Issue Membership Razor/htmx workflow over
  `GetMembershipTypesForIssue`, `PreviewIssueMembership` and the now-atomic
  `IssueMembershipCommand`. Render immutable snapshot/end-date preview,
  require the server-provided negative decision when applicable, accept an
  optional positive UAH cash membership-sale Payment, prevent duplicate
  submit with one idempotency key and reread the canonical Client profile after
  success. Keep `CorrectPayment` and daily-cash Reports for subsequent bounded
  steps.

## Step 108 - Reception Issue Membership workflow

Status: completed. Milestone 7 is in progress.

Plan alignment:

- Complete the reception/UI portion of the roadmap's issue-with-payment task
  after the catalog query, server preview and atomic `IssueMembershipCommand`
  were already proven independently. This step adds no alternate mutation path
  and does not move Membership formulas into Razor or JavaScript.
- Keep the workflow server-authoritative: ordinary issue catalog data comes
  from `GetMembershipTypesForIssue`, all snapshot/date/negative-state values
  come from `PreviewIssueMembership`, and success always rereads the canonical
  Client profile.
- Keep the optional Payment inside the single IssueMembership command and
  idempotency boundary. Standalone Add Payment remains a separate workflow and
  cannot be composed after issue to approximate atomicity.
- Keep `CorrectPayment`, correction UI, daily-cash Reports and the unresolved
  day-close/reconciliation policy for subsequent bounded steps.

Scope:

- Add a profile-owned `IssueMembershipFormViewModel`, input model and
  `_IssueMembershipForm` Razor partial. The form is exposed only by the
  canonical `MembershipActionKeys.Issue` permission and preserves reception
  search context across preview and mutation requests.
- Load active Membership Types and an initial same-day preview while composing
  the profile. Membership Type, start-date and negative-decision changes issue
  htmx GET preview requests and replace only the open action panel.
- Render immutable issue-time name/duration/visit-limit/price snapshot,
  Memberships-owned base/effective end date, initial remaining visits,
  extension days, server warnings and existing negative context. Add primitive
  preview projection properties so generated Razor does not reference the
  Memberships-owned `MembershipCalculatedState` implementation.
- Require one server-provided available negative handling decision where an
  existing active Membership is negative. The accepted v1 `leave_visible`
  option is selectable; deferred coverage/explicit-closure options remain
  visible but disabled, and no Payment silently closes or hides the old state.
- Offer an optional positive cash UAH `membership_sale` Payment prefilled from
  the selected snapshot price. JavaScript only enables/disables its input; the
  handler constructs the canonical `Money` and atomic command input.
- Submit through an antiforgery-protected Razor handler with one idempotency
  key, htmx drop synchronization and disabled/busy duplicate-tap protection.
  Verify command entity/reread targets, then rebuild full canonical profile,
  Membership state and Payment history before showing success.
- Keep antiforgery data out of GET preview query strings by including only
  named `form.*` controls. Adapter validation errors retain the form key and
  entered values; stale/permission/catalog/recalculation failures force a
  canonical workspace refresh.
- Add isolated tablet and phone fixtures plus PostgreSQL readback helpers. The
  tablet path proves payment validation, atomic issue/payment/audits and one
  command key; the phone path proves required negative decision, unchanged old
  negative state and canonical multiple-membership warning.
- Add no EF record, configuration or migration.

Validation:

- Release solution/UI builds passed with 0 warnings/errors. Focused
  `MembershipFormulaOwnershipTests` passed 2/2,
  `MembershipIssuePreviewContractsTests` passed 14/14 and
  `IssueMembershipSmokeTests` passed 2/2 against Docker PostgreSQL at 1024x768
  and 390x844.
- Full-page tablet and phone form/success screenshots were inspected. Snapshot,
  negative decision, optional Payment, errors, touch submit, canonical
  Membership state and Payment history remain readable without overlap or
  horizontal overflow; the snapshot uses two tablet/desktop columns and one
  phone column.
- `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 261 core tests, 35 web tests, 341
  PostgreSQL/architecture infrastructure tests, 35 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` built successfully and
  reported no model drift; this query-contract/Razor workflow generated no
  migration.
- `graphify update .` completed the structural rebuild with 6270 nodes, 13955
  edges and 645 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): add reception issue workflow`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Implement the bounded `CorrectPayment` command/persistence workflow over the
  existing Payment, correction and cancellation source tables: replace/cancel
  modes, required reason, positive cash replacement, canonical Client and
  Membership relationship, retained original fact/status, idempotency,
  append-only audit and canonical Client reread with PostgreSQL rollback
  evidence. Do not invent a closed-day policy while the roadmap decision is
  pending; keep the correction Razor UI and daily-cash Report composition for
  following steps.

## Step 109 - CorrectPayment command and persistence workflow

Status: completed. Milestone 7 is in progress.

Plan alignment:

- Complete the roadmap's bounded `CorrectPayment` command/persistence portion
  after canonical Payment source facts, standalone creation, profile history
  and issue-with-payment are already proven. Keep the correction Razor/htmx UI
  and Reports-owned daily cash composition for later steps.
- Preserve source history: replacement creates a new active cash Payment and a
  `payment_corrections` fact while marking the original `replaced`; cancellation
  creates a `payment_cancellations` fact while marking the original `canceled`.
  Neither path deletes or overwrites the original amount/date/context.
- Follow the accepted open/reconciled-day contract through a narrow Payments
  status-provider port. The current default remains open until day-close
  storage/policy is implemented; a reconciled status is fail-closed to Owner
  and carries `changed_after_close` in command and audit results.
- Keep ordinary corrections outside Membership recalculation. An original or
  replacement `negative_closure` Payment is rejected until its explicit
  Membership-owned correction policy exists.

Scope:

- Add public `CorrectPaymentCommand`, replace/cancel modes, replacement value
  contract and Payment day-reconciliation status/provider contracts.
- Add one transactional command handler that authorizes the canonical active
  Owner/Admin session, normalizes operational metadata, locks the original
  Payment, validates active source state and replacement Client/Membership
  ownership, and rereads the canonical Client after success.
- Persist replacement/correction or cancellation facts, original status,
  command idempotency and one append-only `payment.corrected` or
  `payment.canceled` business audit in the same ACID transaction.
- Include original and replacement Payment summaries, changed field names,
  both occurred dates, reason/comment and changed-after-close state in audit
  evidence. Replay reconstructs and verifies the committed canonical fact.
- Map duplicate/outgoing-fact and PostgreSQL lock/unique races to stable command
  errors. Concurrent same-key submission serializes to one complete workflow;
  audit failure rolls back Payment state, correction/cancellation and
  idempotency together.
- Register the handler and an overridable open-day provider in persistence DI.
  Reuse the existing Payment schema and migration; add no EF model change.
- Add command-contract and PostgreSQL tests for replace/cancel, fallback
  accountability, old/new date explainability, reason/shape/relationship
  validation, negative-closure reservation, day-close permission, idempotent
  and concurrent replay, already-processed state and rollback.

Validation:

- Release solution build passed with 0 warnings/errors.
- Focused `CorrectPaymentCommandContractsTests` passed 5/5.
- Focused PostgreSQL `PostgreSqlCorrectPaymentCommandTests` passed 8/8 against
  Docker PostgreSQL, including replace/cancel facts, reconciled-day Owner
  enforcement, idempotency/concurrency and audit-failure rollback.
- `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 266 core tests, 35 web tests, 349
  PostgreSQL/architecture infrastructure tests, 35 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this command/persistence step generated no migration.
- `graphify update .` completed the structural rebuild with 6410 nodes, 14342
  edges and 645 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(payments): correct and cancel payments`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add the reception `CorrectPayment` Razor/htmx workflow from canonical Client
  Payment history: Owner/Admin replace or cancel on open days, required reason,
  positive cash replacement fields, one idempotency key, busy/disabled submit,
  server errors and a full canonical Client profile reread after success. Keep
  reconciled-day actions Owner-only through the command result/provider, and
  keep daily-cash Report composition as the following bounded step.

## Step 110 - Reception CorrectPayment workflow

Status: completed. Milestone 7 is in progress.

Plan alignment:

- Complete the roadmap's reception/UI portion of `CorrectPayment` after the
  canonical Payment history query and transactional command were proven in
  earlier bounded steps. Keep Reports-owned daily cash composition as the next
  independent task.
- Render correction actions only from per-row server query permissions. Active
  ordinary Payments are Owner/Admin-correctable on open days; reconciled rows
  are Owner-only, and active `negative_closure`, canceled and replaced rows do
  not expose this generic correction action.
- Preserve the command boundary: Razor and JavaScript collect intent only;
  server validation constructs `CorrectPaymentCommand`, permission and source
  state are rechecked transactionally, and every success rereads the complete
  canonical Client profile instead of applying optimistic cash/history state.
- Add no report formula, day-close storage, direct Payment mutation or EF model
  change in this UI step.

Scope:

- Add `payments.correct` query action projection to canonical Client Payment
  rows. Reuse `IPaymentDayReconciliationStatusProvider`, cache status by
  business date during one query and return an explicit
  `day_closed_requires_owner` denial to Admin on reconciled days.
- Add a profile-owned `CorrectPaymentFormViewModel`, input model and
  `_CorrectPaymentForm` partial for each active permitted Payment. Prefill the
  replacement from the original cash fact while excluding
  `negative_closure` from both source and replacement contexts.
- Support replace and cancel modes. Replacement accepts positive UAH amount,
  canonical Client Membership choice, context, occurred UTC time and comment;
  both modes require reason and explicit destructive confirmation while
  preserving the original Payment in history.
- Add an antiforgery-protected Razor handler and htmx island with one
  idempotency key, `hx-sync="this:drop"`, disabled/busy duplicate-tap
  protection and mode-aware replacement controls. Adapter validation retains
  local input; stale, permission, already-processed and other canonical errors
  refresh the full workspace before retry.
- Verify successful command entity relationships, audit reference and
  changed-after-close marker, then rerender Payment history and all allowed
  actions from a fresh `GetClientProfile` result.
- Add tablet replace and phone cancel Playwright paths with validation failure,
  duplicate-tap attempt, canonical correction/cancellation facts, active cash
  state, audit and command-idempotency readback. Update existing Payment
  history/Add Payment smoke locators to distinguish display text from the new
  prefilled closed correction controls.
- Add no EF record, configuration or migration.

Validation:

- Release solution and UI smoke builds passed with 0 warnings/errors. Focused
  PostgreSQL `PostgreSqlGetClientPaymentRowsQueryTests` passed 7/7, including
  open-day permission, reconciled Owner/Admin difference and reserved
  negative-closure behavior.
- Focused `CorrectPaymentSmokeTests` passed 2/2 against Docker PostgreSQL at
  1024x768 and 390x844. Updated reception read-path and Add Payment smoke tests
  each passed 2/2 after display-only locator scoping.
- Full-page tablet/phone form and success screenshots were inspected. Replace
  and cancel controls, validation, warning, confirmation, canonical history,
  touch submit and status rows remain readable without overlap or horizontal
  overflow; cancel mode hides and disables replacement inputs.
- `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 266 core tests, 35 web tests, 350
  PostgreSQL/architecture infrastructure tests, 37 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this query-contract/Razor workflow generated no migration.
- `graphify update .` completed the structural rebuild with 6481 nodes, 14581
  edges and 654 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(payments): add reception correction workflow`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add the Reports-owned daily cash query for a selected business date over
  canonical Payment source facts. Active originals/replacements must drive the
  total, canceled/replaced originals must remain visible and explainable in
  drill-down, and consistency tests must prove total count/sum equals active
  drill-down rows before adding the reception report UI. Keep Membership
  formulas out of Reports and keep day-close storage/policy as a separate
  explicit decision-backed step.

## Step 111 - Daily Payment report source readiness

Status: completed. Milestone 7 is complete.

Plan alignment:

- Close the final Milestone 7 daily-cash source task after Payment storage,
  standalone and issue-integrated creation, canonical Client history,
  correction/cancellation commands and reception workflows are already proven.
- Follow the same bounded source-query pattern established by Step 101 for
  daily Visits. This step exposes canonical Payment rows and derived count/sum
  only; full `GenerateDailyReport`, report navigation and reception report UI
  remain Milestone 9 work.
- Read direct canonical Payment facts as required by ADR-007 and the report
  consistency contract. Active rows drive totals while canceled/replaced rows
  remain visible with their cancellation/correction links.
- Reuse the existing `ix_payments_daily_source` index and Payment source schema;
  add no report-owned formulas outside the returned canonical row set and make
  no EF model or migration change.

Scope:

- Add public `GetDailyPaymentSourceRowsQuery` result/status contracts and a
  `DailyPaymentSourceSnapshot` for one UTC business date. Payment count and cash
  sum are derived from active drill-down rows, so totals cannot diverge from the
  query result.
- Add a PostgreSQL query handler with canonical active-session authorization,
  half-open UTC date bounds, deterministic newest-first ordering, Client display
  names, optional Membership snapshot ownership checks and fail-closed source
  mapping through shared Payment query support.
- Preserve canceled/replaced Payment rows and attach canonical cancellation and
  incoming/outgoing correction facts. A correction that moves `occurred_at`
  leaves the old date explainable and contributes the replacement only to its
  new date.
- Return server-owned correction permissions from the selected day's
  open/reconciled status. Reconciled ordinary Payments are Owner-only and the
  reserved `negative_closure` context exposes no generic correction action.
- Reject an aggregate with mixed active currencies instead of producing one
  misleading cash sum. Empty days retain the v1 reception default `UAH` zero.
- Register the handler in persistence DI. Queries remain read-only and create no
  business audit entry.
- Add PostgreSQL integration coverage for count/sum equality, retained
  correction/cancellation drill-down, cross-date corrections, UTC boundaries,
  deterministic ordering, empty dates, active actor authorization,
  reconciled-day permissions, reserved negative closure, source inconsistency,
  DI registration and `ix_payments_daily_source` query-plan usage.

Validation:

- Release infrastructure-test build passed with 0 warnings/errors.
- Focused `PostgreSqlGetDailyPaymentSourceRowsQueryTests` passed 7/7 against
  Docker PostgreSQL, including canonical total/drill-down equality and
  old/new-date correction explainability.
- `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 266 core tests, 35 web tests, 357
  PostgreSQL/architecture infrastructure tests, 37 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this query-contract step generated no migration.
- `graphify update .` completed the structural rebuild with 6545 nodes, 14770
  edges and 647 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(payments): add daily cash source query`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Start Milestone 8 with one bounded `AddFreeze` command/persistence workflow
  over the existing Freeze source tables and Memberships recalculation
  coordinator: inclusive range validation, canonical Membership lock,
  idempotency, extension-day rebuild, append-only `freeze.added` audit and
  rollback evidence. Keep `CancelFreeze`, reception UI and Owner-only
  NonWorkingDay preview/confirmation as following independent steps.

## Step 112 - Freeze range eligibility decision

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Audited Milestone 8 before implementation and found its explicit dependency
  on a product decision for Freeze range validation. ADR-014 had already
  resolved the Visit-side Freeze block, but the inverse AddFreeze eligibility
  boundary remained open in the domain model, implementation plan and roadmap.
- Close that decision before writing AddFreeze code, so Infrastructure or Razor
  code cannot invent lifecycle, date-bound or Visit-conflict behavior.
- Keep the still-unresolved NonWorkingDay application-scope decision open and
  make no NonWorkingDay implementation claim in this step.
- Make no code, schema, migration or UI change. This is the bounded decision
  gate required before the first Milestone 8 implementation slice.

Scope:

- Add accepted ADR-015. AddFreeze targets a lifecycle-active Membership; its
  inclusive start is bounded by Membership start and locked pre-command
  canonical effective end, while an eligible end may cross that date and is
  never silently clipped.
- Define symmetric Visit/Freeze integrity: an active counted Membership Visit
  inside the proposed range returns stable `freeze_conflicts_with_visit`.
  Canceled and one_off/trial Visits do not block the range.
- Require one Membership-first lock order across MarkVisit, AddFreeze and future
  CancelFreeze work. Source fact, synchronous recalculation, business audit and
  idempotency success remain one PostgreSQL transaction.
- Preserve overlapping Freeze/NonWorking sources as valid source facts while
  Memberships counts their union of unique active calendar dates.
- Synchronize ADR index, AGENTS guardrails, architecture baseline, domain/data
  models, command/error contracts, reception UI workflow, implementation plan,
  roadmap, vertical-slice plan and the four relevant local skill source maps.

Validation:

- ADR-014/ADR-015 local references exist; stale ADR range and unresolved Freeze
  policy references are absent from the synchronized governing documents.
- `git diff --check` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 266 core tests, 35 web tests, 357
  PostgreSQL/architecture infrastructure tests, 37 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this documentation decision generated no migration.
- `graphify . --update` was attempted for the governing documentation changes
  but stopped because no semantic extraction LLM backend is configured. It made
  no `graphify-out/` change, so this step needs no generated graph commit.

Commit:

- `docs(freezes): define range eligibility policy`.

Next recommended step:

- Add one Memberships-owned pure Freeze eligibility contract and focused core
  tests for lifecycle status, inclusive endpoints, before-start/post-expiry
  rejection, end-after-effective acceptance and active/canceled counted Visit
  overlap. Keep PostgreSQL AddFreeze orchestration, migration changes,
  CancelFreeze and reception UI as later bounded steps.

## Step 113 - Pure Membership Freeze eligibility contract

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Implement only the ADR-015 pure Memberships boundary required before
  AddFreeze persistence. Keep source insertion, command authorization,
  idempotency, audit, DI, PostgreSQL locking and UI outside this step.
- Reuse canonical `MembershipVisitSourceFact` inputs because they represent
  counted Membership consumptions. One-off/trial Visits do not enter this
  boundary, while canceled counted facts remain visible but non-blocking.
- Preserve Memberships ownership of lifecycle/date eligibility and return
  typed status plus stable domain error strings for the future command adapter.
  Application `CommandErrorCode` mapping remains part of the AddFreeze command
  step rather than being exposed without a command consumer.
- Add no EF record, configuration or migration and make no change to the
  existing Freeze source schema or extension calculation.

Scope:

- Add `MembershipFreezeEligibilityPolicy` with explicit Membership id,
  issue-time terms, canonical calculated state, lifecycle, proposed `DateRange`
  and retained Membership Visit source inputs.
- Add a canonical `MembershipStateReadModel` overload matching the established
  Membership Visit eligibility shape. The current query date does not replace
  the locked pre-command effective-end boundary for backdated Freeze intent.
- Return immutable Membership id/range, typed eligible/inactive/before-start/
  after-effective/conflicting-Visit status and stable
  `membership_not_eligible` or `freeze_conflicts_with_visit` error strings.
- Accept both inclusive start bounds and retain an eligible end beyond the
  pre-command effective end without clipping. Reject canceled/corrected
  Membership lifecycle and a start outside the canonical effective interval.
- Reject an active counted Visit on either inclusive range endpoint. Ignore
  canceled counted Visits and active counted Visits outside the range while
  fail-closing on missing, foreign or duplicate canonical source inputs.
- Add 10 focused core cases covering the complete pure ADR-015 matrix and the
  canonical read-model overload.

Validation:

- Focused `MembershipFreezeEligibilityPolicyTests` passed 10/10.
- Full `BodyLife.Crm.Tests` validation passed 276/276.
- `git diff --check` passed before the progress update.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 276 core tests, 35 web tests, 357
  PostgreSQL/architecture infrastructure tests, 37 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this pure domain step generated no migration.
- Standard `graphify update .` reached the known Python 3.14 `forkserver`
  filesystem limitation (`Errno 95`). Re-running the same structural update in
  `fork` mode completed with 6594 nodes, 14879 edges and 655 communities.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): define freeze eligibility`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add one locked PostgreSQL Membership Freeze eligibility preparation boundary:
  lock the selected issued Membership first, rebuild/read canonical state, load
  retained counted Visit sources, invoke the pure policy and return its typed
  result without inserting a Freeze. Keep the transactional AddFreeze source/
  recalculation/audit/idempotency workflow as the following bounded step.

## Step 114 - Locked PostgreSQL Freeze eligibility preparation

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Implement the ADR-015 persistence preparation boundary immediately after the
  pure Membership Freeze eligibility contract from Step 113 and before the
  state-changing AddFreeze command.
- Follow the established MarkVisit preparation shape: require a caller-owned
  PostgreSQL transaction, lock the selected issued Membership first, rebuild
  canonical Membership state and return a typed preparation result.
- Add the ADR-015 symmetric concurrency protection by locking the retained
  counted Visit and Visit-consumption source rows relevant to the proposed
  inclusive range after the Membership lock.
- Keep authorization, command metadata, idempotency, Freeze insertion,
  extension recalculation after mutation, business audit, DI and reception UI
  outside this bounded step. No EF model or migration change is required.

Scope:

- Add `MembershipFreezeEligibilityPreparer` with required Client/Membership
  identifiers, caller-owned transaction enforcement and Membership-first
  `FOR UPDATE` selection by canonical Client ownership.
- Rebuild/read the locked Membership state through
  `MembershipStateCacheRebuilder`, reconstruct immutable issue-time terms and
  map the retained lifecycle status before invoking the pure
  `MembershipFreezeEligibilityPolicy`.
- Load only counted Membership Visit sources in the requested UTC business-date
  range and lock both `visits` and `visit_consumptions` in deterministic order.
  Preserve canceled source history while active counted rows drive conflicts.
- Extract the existing fail-closed Visit/consumption source mapper from the
  cache rebuilder so canonical rebuild and Freeze preparation interpret active,
  canceled and replacement consumption history identically.
- Return immutable prepared/not-found status, selected identifiers, pure
  eligibility and rebuild status. The preparation boundary never inserts a
  Freeze and leaves commit or rollback ownership with its caller.
- Add five PostgreSQL integration cases for input/transaction guards,
  wrong-selection `NotFound`, both inclusive Membership boundaries plus an
  unclipped end, canceled/out-of-range/active-endpoint Visit behavior, no Freeze
  insertion, rollback of the rebuilt cache and real Membership/Visit/
  consumption row-lock release.

Validation:

- Release infrastructure-test build passed with 0 warnings/errors.
- Focused Freeze eligibility preparation tests passed 5/5 against Docker
  PostgreSQL, including `55P03` lock evidence for the Membership, Visit and
  Visit-consumption rows and successful updates after caller rollback.
- `git diff --check` passed before the progress update.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 276 core tests, 35 web tests, 362
  PostgreSQL/architecture infrastructure tests, 37 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this persistence preparation step generated no migration.
- `graphify update .` completed the structural rebuild with 6632 nodes, 14990
  edges and 655 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(memberships): prepare freeze eligibility`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add one transactional `AddFreeze` command/persistence workflow over this
  preparation boundary and the existing Freeze source tables: canonical actor
  authorization and metadata, idempotency, active Freeze insertion, synchronous
  extension-day recalculation, append-only `freeze.added` business audit,
  canonical reread target and rollback evidence. Keep CancelFreeze and reception
  UI as later independent steps.

## Step 115 - Transactional AddFreeze workflow

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Implement the state-changing `AddFreeze` slice immediately after the pure
  eligibility contract and locked PostgreSQL preparation from Steps 113-114.
- Reuse the existing `freezes`, `membership_extension_days`, business-audit and
  command-idempotency schema. This step adds no EF model or migration change.
- Keep Memberships as the sole owner of extension union, effective end and
  warning calculations. Freezes exposes locked canonical source ranges but does
  not calculate Membership state itself.
- Keep `CancelFreeze`, reception UI and Owner-only NonWorkingDay workflows as
  following bounded Milestone 8 steps.

Scope:

- Add the public `AddFreezeCommand` with canonical Client reread and related
  Membership target, plus stable `FreezeConflictsWithVisit` command mapping.
- Require an active canonical Owner or Admin account/session and normalize the
  correlation id, idempotency key, device label, occurred time, reason, comment,
  entry origin and optional backfill/fallback batch metadata.
- Execute authorization, idempotency replay/rejection, Membership-first locked
  eligibility preparation, active Freeze insertion, synchronous Membership
  recalculation, `freeze.added` business audit and idempotency completion in one
  PostgreSQL transaction.
- Add a Memberships extension-source provider contract and a PostgreSQL Freeze
  reader that locks all retained ranges for the selected Membership. Active
  ranges participate in the union while canceled source facts remain visible
  but non-extending.
- Make the canonical cache rebuild combine opening/adjustment extension days
  with the union of active date-range sources, atomically replace explanation
  rows and persist cache version 5. Existing Visit and negative-balance state is
  preserved through the same Memberships calculator boundary.
- Return a canonical Client reread target after success and map duplicate,
  Membership/Visit/Freeze concurrency, eligibility and recalculation failures
  without leaving partial Freeze, cache, extension-day, audit or idempotency
  state.
- Add focused domain, architecture and PostgreSQL coverage for union arithmetic,
  source ownership, permissions, metadata, Visit conflicts, lifecycle/range
  rejection, replay, concurrent duplicate submission, lock conflict, audit and
  recalculation rollback, and DI resolution.

Validation:

- Focused Membership extension calculation tests passed 11/11.
- Focused `PostgreSqlAddFreezeCommandTests` passed 11/11 against Docker
  PostgreSQL, and the compatibility selection for Membership formula ownership,
  cache rebuild, Freeze storage and MarkVisit passed 46/46.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 279 core tests, 35 web tests, 373
  PostgreSQL/architecture infrastructure tests, 37 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  this workflow reuses the existing Freeze source schema.
- `git diff --check` passed before the progress update.
- `graphify update .` completed the structural rebuild with 6761 nodes, 15397
  edges and 654 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(freezes): add freeze workflow`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Add one transactional `CancelFreeze` workflow over the retained
  `freeze_cancellations` source table: canonical Owner/Admin authorization,
  Membership-first locking, active Freeze validation, idempotency, retained
  cancellation fact, synchronous extension-union rebuild, append-only
  `freeze.canceled` audit, canonical Client reread and rollback/concurrency
  evidence. Keep reception UI and NonWorkingDays as later independent steps.

## Step 116 - Transactional CancelFreeze workflow

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Implement command number 13, `CancelFreeze`, immediately after the completed
  `AddFreeze` workflow and before NonWorkingDay commands in the accepted command
  order.
- Reuse the existing `freezes`, `freeze_cancellations`, extension explanation,
  business-audit and command-idempotency schema. This step adds no EF model or
  migration change.
- Preserve ADR-014/ADR-015 Membership-first ordering across MarkVisit,
  AddFreeze and CancelFreeze, and keep Memberships as the only calculator of
  extension union, effective end and warnings.
- Keep reception UI and NonWorkingDay implementation outside this bounded step.
  The unresolved NonWorkingDay application-scope decision remains a required
  gate before its preview/add/correct workflows.

Scope:

- Add the public `CancelFreezeCommand`, canonical Freeze cancellation source
  shape and day-reconciliation abstraction. The default provider reports an
  open day until a real reconciliation source is introduced.
- Add `CancelFreezeSourcePreparer`: resolve the selected Membership through the
  immutable Freeze relationship, lock that Membership first, then lock the
  Freeze and retained cancellation source rows, and return typed prepared,
  not-found, already-canceled or inconsistent status.
- Require a canonical active Owner/Admin account and session, normalized
  correlation/idempotency/device metadata, `occurred_at`, reason/comment, entry
  origin and optional backfill/fallback batch. Reconciled-day cancellation is
  Owner-only and carries `changed_after_close`.
- Execute idempotency checks, locked source preparation, pre-command canonical
  recalculation/reread, active-to-canceled Freeze transition, retained
  `freeze_cancellations` insert, post-command recalculation/reread,
  `freeze.canceled` audit and idempotency completion in one PostgreSQL
  transaction.
- Keep canceled Freeze dates in `membership_extension_days` as inactive
  explanation rows while removing their contribution from the active union.
  Overlapping active Freeze sources continue to determine the canonical union;
  the command never subtracts a naive range length or edits effective end.
- Return the cancellation id, related source Freeze and canonical Client reread
  target. Map duplicate, already-canceled, day-close and row-lock conflicts to
  stable command errors without partial source, cache, audit or idempotency
  state.
- Register the command handler, Membership-first source preparer and open-day
  reconciliation provider in the persistence composition root.

Validation:

- Release infrastructure-test build passed with 0 warnings/errors.
- Focused `PostgreSqlCancelFreezeCommandTests` passed 13/13 against Docker
  PostgreSQL, covering retained inactive explanations, overlap union, Owner and
  both Admin account types, reconciled-day policy, fallback metadata,
  not-found/already-canceled behavior, replay, concurrent same-key
  serialization, real Membership/Freeze row locks, recalculation/audit rollback
  and DI resolution.
- The focused compatibility selection for Membership formula ownership,
  AddFreeze, Freeze storage/projection, canonical cache rebuild and MarkVisit
  passed 57/57.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1
  BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55532;
  Database=postgres;Username=bodylife;Password=bodylife_dev_password'
  ./scripts/validate.sh` passed: Release build 0 warnings/errors,
  formatting/analyzers, 279 core tests, 35 web tests, 386
  PostgreSQL/architecture infrastructure tests, 37 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` reported no model drift;
  cancellation uses the existing Freeze source schema.
- `git diff --check` passed before the progress update.
- `graphify update .` completed the structural rebuild with 6896 nodes, 15829
  edges and 675 communities; optional HTML visualization remained skipped above
  its configured 5000-node limit.
- `graphify . --update` was attempted for the progress documentation change but
  stopped because no semantic extraction LLM backend is configured.

Commits:

- `feat(freezes): cancel freeze workflow`.
- `chore(graphify): refresh code graph`.

Next recommended step:

- Resolve the remaining Milestone 8 NonWorkingDay application-scope decision in
  one accepted ADR and synchronize the governing docs: whether a period extends
  only overlapping eligible Membership calendar dates or contributes its full
  period once any overlap exists, plus the exact affected-membership lifecycle
  boundary. Keep schema, `PreviewNonWorkingDayImpact`, add/correct commands and
  UI for later implementation steps after that decision is explicit.

## Step 117 - NonWorkingDay application-scope decision

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Close the remaining documented Milestone 8 product-decision dependency before
  adding NonWorkingDay domain code, schema, preview, commands or UI.
- Preserve ADR-005 Memberships formula ownership, ADR-012 Owner-only authority
  and the locked pre-command-state precedent from ADR-015.
- Keep this step architecture/documentation-only so implementation can follow in
  small, test-first slices.

Scope:

- Accept ADR-016 for NonWorkingDay application scope and full-period
  contribution.
- A proposed period affects an issued Membership only when its lifecycle status
  is `active` and it has any inclusive overlap with canonical pre-command state
  calculated without the proposed/replaced period. Client operational status
  and current server date do not replace that policy.
- Every confirmed application contributes the complete inclusive period without
  clipping to Membership start or locked pre-command effective end. Memberships
  still de-duplicates overlapping Freeze/NonWorkingDay/adjustment dates by union.
- Define successful scope as an immutable Owner-confirmed transaction snapshot.
  Preview token/fingerprint binds the exact ordered Membership IDs and applied
  ranges; expiry or any revalidation difference fails without partial writes.
- Define correction behavior: reason-only preserves scope, range replacement
  computes a new snapshot with the old source excluded, cancellation retains old
  facts, and recalculation covers the union of old/new Membership IDs.
- Synchronize ADR index, architecture baseline, domain/data/interaction/UI
  contracts, implementation plan/roadmap, AGENTS guardrails and relevant local
  skill source maps. Add no production code, EF mapping, migration or UI.

Validation:

- Stale-decision search found no remaining `001..015` ADR range, unresolved
  NonWorkingDay application-scope question or conditional later-membership
  policy in the governing docs/source maps.
- `git diff --check` passed before the progress update.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed after
  starting Docker Desktop and the healthy repo PostgreSQL service. The script
  read `ConnectionStrings:BodyLifeTestAdmin` from
  `appsettings.Development.json`: Release build 0 warnings/errors,
  formatting/analyzers, 279 core tests, 35 web tests, 386
  PostgreSQL/architecture infrastructure tests, 37 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `graphify . --update` was attempted for the project-knowledge documentation
  change but stopped because no semantic extraction LLM backend is configured.

Commit:

- `docs(nonworking-days): define application scope`.

Next recommended step:

- Add one pure Memberships-owned NonWorkingDay eligibility/contribution contract
  and focused domain tests for lifecycle-active status, both inclusive overlap
  endpoints, no-overlap rejection, full-period contribution when Membership
  starts/ends inside the period, proposed-source exclusion and deterministic
  applied ranges. Keep EF schema, preview tokens, PostgreSQL scope selection,
  commands and UI as later independent steps.

## Step 118 - Pure NonWorkingDay application policy

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Implement the first ADR-016 code slice immediately after accepting its product
  decision, before persistence, preview tokens, scope selection or commands.
- Keep eligibility and full-period contribution inside Memberships so
  NonWorkingDays, Infrastructure and UI cannot reinterpret the date formula.
- Mirror the existing pure Freeze policy/result convention without adding Visit
  conflict inputs that do not belong to global NonWorkingDay eligibility.

Scope:

- Add `MembershipNonWorkingDayApplicationPolicy` with canonical read-model and
  lower-level issue-terms/pre-command-state overloads.
- Include only lifecycle-active Memberships whose inclusive source interval
  overlaps the proposed period: period end is on/after Membership start and
  period start is on/before pre-command effective end.
- Return explicit inactive, period-before-start and period-after-effective-end
  statuses for excluded Memberships. Excluded results expose no applied range
  and zero applied days.
- For every eligible Membership, expose the original complete period as
  `AppliedRange` and its inclusive length as `AppliedDays`; do not clip either
  boundary to Membership dates.
- Make the pre-command-state dependency explicit so a proposed period cannot
  extend effective end and thereby make itself eligible.
- Add focused unit coverage for both inclusive overlap endpoints, full-period
  contribution before Membership start and after effective end, inactive
  lifecycle states, both no-overlap directions, proposed-source exclusion,
  canonical stored effective end and invalid canonical inputs.
- Add no NonWorkingDay EF record, migration, PostgreSQL selector, preview/token,
  command handler, audit or UI change.

Validation:

- Focused `MembershipNonWorkingDayApplicationPolicyTests` passed 9/9 in Release.
- `dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal
  --no-restore` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed. The
  script read `ConnectionStrings:BodyLifeTestAdmin` from
  `appsettings.Development.json`: Release build 0 warnings/errors,
  formatting/analyzers, 288 core tests, 35 web tests, 386
  PostgreSQL/architecture infrastructure tests, 37 Playwright smoke tests and
  EF migration listing through `20260715213519_AddPaymentSourceFacts`.
- `graphify update .` and its no-cluster fallback were attempted in sandboxed
  and approved modes, but the local graphify rebuild stopped with
  `Errno 95: Operation not supported`. Its partial cache-index change was
  removed; no generated graph update is claimed or committed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(memberships): define nonworking day application policy`.

Next recommended step:

- Add only the PostgreSQL/EF source schema for `non_working_periods`,
  `non_working_period_applications` and `non_working_period_cancellations`, with
  one reviewable migration and focused PostgreSQL tests for inclusive ranges,
  lifecycle/status values, full-period application equality, composite
  Membership/Client ownership, one active application per period/version and
  retained cancellation history. Keep preview scope selection/tokens,
  Add/Correct commands, recalculation orchestration and UI for later steps.

## Step 119 - NonWorkingDay PostgreSQL source storage

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Implement only the three NonWorkingDay source tables required by the first
  Milestone 8 task after the pure ADR-016 policy from Step 118.
- Keep the stored application set as the immutable confirmed transaction
  snapshot while leaving scope selection, preview fingerprints/tokens,
  state-changing commands, Membership recalculation orchestration and UI for
  later bounded steps.
- Follow the accepted data-architecture field outline and existing
  Freeze/Payment source-fact conventions without inventing a reason-code
  vocabulary or permission behavior inside EF configuration.

Scope:

- Add EF records/configurations for `non_working_periods`,
  `non_working_period_applications` and
  `non_working_period_cancellations` under the NonWorkingDays persistence
  boundary.
- Constrain period ranges to inclusive `start_date <= end_date`, require a
  non-empty reason code, reject blank optional comments and restrict lifecycle
  status to `active`, `canceled` or `corrected`.
- Add alternate period identity `(id, start_date, end_date)` and reference it
  from each application through `(non_working_period_id, applied_start_date,
  applied_end_date)`. PostgreSQL therefore rejects a clipped or widened
  application range instead of relying on caller discipline for ADR-016's
  full-period rule.
- Add composite `(membership_id, client_id)` ownership against
  `issued_memberships`, preview-before-confirm ordering and one partial unique
  active application per period/version and Membership. Canceled/corrected
  application rows remain insertable and explainable.
- Add retained cancellation facts with non-empty reason, restrictive foreign
  keys and at most one cancellation per period; no source-history delete path
  cascades from a period, Membership, account or session.
- Add reviewable migration
  `20260717072704_AddNonWorkingDaySourceFacts` and four focused PostgreSQL tests
  for DDL/indexes, inclusive ranges, exact full-period equality, cross-Client
  ownership rejection, lifecycle values, active uniqueness and retained
  cancellation history.
- Add no scope reader, preview/token contract, Add/Correct command, extension
  source provider, recalculation hook, business audit or UI behavior.

Validation:

- Focused `PostgreSqlNonWorkingDaysStorageTests` passed 4/4 in Release against
  local Docker PostgreSQL; each test applied all migrations to a fresh database.
- `dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal
  --no-restore` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 288 core tests, 35 web
  tests, 390 PostgreSQL/architecture infrastructure tests, 37 Playwright smoke
  tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- Rebuilt `dotnet-ef migrations has-pending-model-changes` passed with no model
  changes since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was removed, so no generated graph update is claimed in this step.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(nonworking-days): add source storage`.

Next recommended step:

- Add only a PostgreSQL-backed NonWorkingDay implementation of
  `IMembershipExtensionSourceProvider`: read full applied ranges for a
  Membership only when both period and application are active, register it
  beside the Freeze provider, bump the rebuild version and prove active/inactive
  filtering plus Freeze/NonWorkingDay union in focused recalculation tests. Keep
  preview scope selection/tokens, Add/Correct commands, audit and UI for later
  bounded steps.

## Step 120 - NonWorkingDay Membership extension source

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Implement only the NonWorkingDay-to-Memberships recalculation adapter
  recommended after Step 119, before affected-scope preview, confirmation
  tokens or state-changing commands.
- Keep NonWorkingDays responsible for canonical period/application source rows
  while Memberships remains the sole owner of calendar-day union, effective end
  date, derived cache and explanation rows.
- Reuse the existing `IMembershipExtensionSourceProvider` boundary and Freeze
  provider composition instead of adding a second extension formula.

Scope:

- Add `MembershipNonWorkingDayExtensionSourceReader` as a transaction-bound
  PostgreSQL implementation of `IMembershipExtensionSourceProvider`.
- Require a caller-owned transaction after the selected Membership has been
  locked. Read retained application/period rows in deterministic range, period
  and application order and lock both rows with `FOR UPDATE` so recalculation
  cannot observe a concurrent lifecycle correction halfway through its source
  snapshot.
- Map each application row to source type `non_working_period`, use the
  application ID as the unique explanation identity and preserve the exact
  stored full applied range. Build a bounded label from period range, reason
  code and optional comment.
- Mark contribution active only when both the application and parent period are
  `active`; retained `canceled`/`corrected` rows remain inactive explanation
  sources instead of disappearing from rebuildable history.
- Register the reader beside `MembershipFreezeExtensionSourceReader` so the
  production `MembershipStateCacheRebuilder` composes both source families.
  Increment recalculation version from 5 to 6 because canonical derived state
  now includes NonWorkingDay applications.
- Add two focused PostgreSQL tests for input/transaction guards, deterministic
  lifecycle mapping, period/application row locks, full source identity/range,
  Freeze/NonWorkingDay overlap union, inactive explanation persistence and
  cache/version repair. Update the existing DI assertion to require both
  extension providers.
- Add no EF record/configuration or migration change, affected-scope selector,
  preview/fingerprint/token contract, Add/Correct command, audit or UI.

Validation:

- Focused `PostgreSqlNonWorkingDayExtensionSourceTests` passed 2/2 in Release
  against local Docker PostgreSQL; the updated extension-provider DI assertion
  passed 1/1.
- `dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal
  --no-restore` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 288 core tests, 35 web
  tests, 392 PostgreSQL/architecture infrastructure tests, 37 Playwright smoke
  tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- Rebuilt `dotnet-ef migrations has-pending-model-changes` passed with no model
  changes since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 95: Operation not supported`; its partial cache-index
  change was removed, so no generated graph update is claimed in this step.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(memberships): include nonworking day extensions`.

Next recommended step:

- Add only an internal PostgreSQL affected-scope preparation boundary for a
  proposed new NonWorkingDay period: use a caller-owned consistent transaction,
  select lifecycle-active Membership candidates in deterministic order, rebuild
  canonical pre-command state from currently accepted sources, invoke the pure
  ADR-016 policy and return the exact ordered Membership/Client/full-range set
  without writing source/cache/audit rows. Keep public preview tokens,
  Add/Correct commands, recalculation orchestration and UI for later steps.

## Step 121 - NonWorkingDay affected Membership scope preparation

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Implement only the internal PostgreSQL preparation boundary recommended after
  Step 120, before public preview/token, Add/Correct command, recalculation
  orchestration, audit or UI work.
- Keep NonWorkingDays responsible for period/application source facts while
  Memberships owns canonical state calculation and the ADR-016 eligibility/full-
  period policy. Expose the result through a narrow approved Memberships
  contract instead of letting NonWorkingDays reference formula implementations.
- Require a caller-owned `RepeatableRead` or `Serializable` transaction so the
  ordered candidate set and all canonical source reads share one consistent
  PostgreSQL snapshot.

Scope:

- Add `IMembershipNonWorkingDayAffectedScopePreparer` and immutable
  `MembershipNonWorkingDayAffectedScope` contract models. The contract requires
  non-empty Membership/Client identities, unique Memberships, deterministic
  Membership-id order and the exact full proposed period on every result item.
- Add `MembershipNonWorkingDayAffectedScopePreparer` under Memberships
  persistence and register the concrete/scoped interface pair. It locks every
  lifecycle-active issued Membership in `id` order with `FOR UPDATE`, calculates
  canonical pre-command state from current opening, adjustment, Visit, Freeze
  and accepted NonWorkingDay sources, invokes the pure ADR-016 policy and returns
  only eligible Membership/Client/full-range rows.
- Split the read-only canonical calculation phase inside
  `MembershipStateCacheRebuilder` from its existing cache/explanation persistence
  phase. Ordinary rebuild behavior remains unchanged, while affected-scope
  preparation can reuse the same source-based formulas without `SaveChanges`.
- Extend the Membership formula ownership gate with only the new interface and
  scope read models as approved cross-module contracts. The first full gate
  correctly rejected a preparer placed under NonWorkingDays because it referenced
  Memberships policy/state implementations; the implementation was moved back
  under the Memberships owner before final validation.
- Add three focused tests for DI identity, transaction/isolation guards and the
  exact PostgreSQL scope. The database scenario covers both inclusive endpoints,
  an inactive Client, a canceled Membership, a no-overlap active candidate,
  accepted Freeze extension over a deliberately stale cache, deterministic
  output, active-candidate row locks and unchanged source/cache/extension/audit/
  idempotency state observed inside the caller transaction.
- Add no EF model/migration, public preview query, fingerprint/token, period or
  application write, command, audit event, recalculation orchestration or UI.

Validation:

- Focused `PostgreSqlNonWorkingDayAffectedScopePreparerTests` passed 3/3 in
  Release against local Docker PostgreSQL; the focused Membership formula
  ownership checks passed 2/2 after correcting the module boundary.
- `dotnet format BodyLife.Crm.sln --verbosity minimal --no-restore` passed and
  the final validation gate's `--verify-no-changes` check also passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 288 core tests, 35 web
  tests, 395 PostgreSQL/architecture infrastructure tests, 37 Playwright smoke
  tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- Rebuilt `dotnet-ef migrations has-pending-model-changes` passed with no model
  changes since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was removed, so no generated graph update is claimed in this step.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(memberships): prepare nonworking day scope`.

Next recommended step:

- Add only a tamper-resistant, expiring NonWorkingDay preview confirmation
  token/fingerprint foundation over normalized proposed period/reason input and
  the exact ordered Membership/Client/full-range scope. Cover deterministic
  fingerprints, expiry, tampering, key/configuration validation and scope/input
  mismatch without yet adding the public preview query, Add/Correct commands,
  source/cache/audit writes or UI.

## Step 122 - NonWorkingDay preview confirmation token foundation

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Implement only the tamper-resistant preview confirmation foundation
  recommended after Step 121 and required by ADR-016 before the public
  `PreviewNonWorkingDayImpact` query or any NonWorkingDay command.
- Bind the normalized proposed period/reason input to the exact deterministic
  Membership/Client/full-range scope already owned and prepared by Memberships.
- Keep the token advisory and short-lived. No token can replace the command's
  future consistent-transaction scope revalidation.

Scope:

- Add normalized `NonWorkingDayPreviewInput`, immutable confirmation and
  validation result contracts plus `INonWorkingDayPreviewTokenService` under
  NonWorkingDays. Reason code/comment values are trimmed, NFC-normalized and
  length-bounded before fingerprinting.
- Add a versioned canonical JSON SHA-256 scope fingerprint over the proposed
  period/reason and every exact ordered Membership id, Client id and full
  applied range. Any input, identity or range change produces
  `InputOrScopeMismatch`.
- Add an HMAC-SHA256 confirmation token with authenticated fingerprint,
  millisecond UTC issue time and a bounded expiry. Validation uses canonical
  token encoding and fixed-time signature/fingerprint comparisons, rejects
  malformed/tampered/future-issued tokens and reports expiry at the exact
  boundary before stale-scope comparison.
- Add configuration validation under
  `BodyLife:NonWorkingDayPreviewToken`: a secret canonical Base64 signing key of
  32-64 bytes is required when the service is resolved; lifetime defaults to
  five minutes and must be a whole second from one through thirty minutes. The
  singleton registration is lazy so this preparatory step adds no committed
  development or production secret.
- Add eight focused infrastructure tests for deterministic fingerprints,
  Unicode/whitespace normalization, expiry, tampering, malformed/wrong-key
  tokens, all bound input/scope changes, contract bounds, configuration errors
  and lazy singleton DI behavior.
- Add no EF model/migration, PostgreSQL writes, public preview query, Owner
  authorization, Add/Correct command, source/cache/audit mutation or UI.

Validation:

- Focused `NonWorkingDayPreviewTokenServiceTests` passed 8/8 in Release.
- Scoped `dotnet format BodyLife.Crm.sln --no-restore
  --verify-no-changes --include ...` passed for every file in this step.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 288 core tests, 35 web
  tests, 403 PostgreSQL/architecture/security infrastructure tests, 37
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- Rebuilt `dotnet-ef migrations has-pending-model-changes` passed with no model
  changes since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was removed, so no generated graph update is claimed in this step.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(nonworking-days): sign preview confirmation tokens`.

Next recommended step:

- Implement only the Owner-authorized `PreviewNonWorkingDayImpact` query as a
  read-only consistent transaction: prepare the exact ADR-016 scope, obtain
  Memberships-owned before/after extension estimates and overlap warnings,
  return the ordered Membership/Client/full-range details, and issue this
  expiring token/fingerprint. Keep Add/Correct commands, period/application
  writes, recalculation persistence, audit and UI for later bounded steps.

## Step 123 - Owner NonWorkingDay impact preview query

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Implement the roadmap's `PreviewNonWorkingDayImpact` application boundary
  after exact affected-scope preparation and signed confirmation foundations,
  before any Add/Correct mutation or UI work.
- Keep eligibility, extension union, effective-end estimates and overlap
  interpretation inside Memberships. NonWorkingDays coordinates authorization,
  normalized input, output mapping and token issuance only.
- Treat preview as an advisory read: use a consistent snapshot, return no
  success if canonical calculation fails, create no business audit and leave
  all source, derived cache and idempotency state unchanged.

Scope:

- Add a Memberships-owned pure impact estimator. It compares canonical current
  extension/effective-end state with the proposed full period, counts only new
  unique active date-source days, excludes inactive sources and returns
  deterministic per-source Freeze/NonWorkingDay overlap warnings.
- Add `IMembershipNonWorkingDayImpactPreparer` and exact preparation/item read
  models. Extend the existing affected-scope preparer so one candidate lock and
  canonical calculation pass produces both the immutable ordered scope and its
  before/after estimates under the existing `RepeatableRead`/`Serializable`
  requirement.
- Add `PreviewNonWorkingDayImpactQuery`, status/result and preview read models.
  Output includes normalized period/reason values, exact ordered Membership and
  Client IDs, full applied ranges, before/estimated-after extension days and
  effective end dates, unique added/overlap days, overlap details, affected
  count and signed fingerprint/token expiry.
- Add `PreviewNonWorkingDayImpactQueryHandler`: require a canonical active Owner
  account/session, validate input before persistence work, open one owned
  `RepeatableRead` transaction, prepare impact, issue the Step 122 token over the
  exact scope and commit only the read snapshot. Canonical calculation failures
  return stable `recalculation_failed` without issuing a successful preview.
- Register the impact preparer as the same scoped instance as the exact-scope
  preparer and register the public query handler. No signing key is committed;
  the existing lazy secret configuration remains unchanged.
- Add three pure estimator tests and expand the PostgreSQL scope suite from
  three to five tests. Coverage proves full-period addition, Freeze/
  NonWorkingDay union, inactive exclusion, deterministic warnings, overflow and
  inconsistent-state guards, DI identity, transaction isolation, Owner-only
  access, input validation, stable calculation failure, exact output, valid
  bound token and unchanged source/cache/audit/idempotency state.
- Extend the Membership formula ownership gate only with the new read-only
  Memberships contracts consumed by NonWorkingDays.
- Add no EF model/migration, NonWorkingDay period/application write,
  recalculation persistence, audit entry, idempotency record, Add/Correct
  command, appsettings secret or UI endpoint.

Validation:

- Focused `MembershipNonWorkingDayImpactEstimatorTests` passed 3/3;
  `MembershipFormulaOwnershipTests` passed 2/2; expanded
  `PostgreSqlNonWorkingDayAffectedScopePreparerTests` passed 5/5 in Release
  against local Docker PostgreSQL.
- Scoped `dotnet format BodyLife.Crm.sln --no-restore
  --verify-no-changes --include ...` passed for every file in this step.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 291 core tests, 35 web
  tests, 405 PostgreSQL/architecture/security infrastructure tests, 37
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- Rebuilt `dotnet-ef migrations has-pending-model-changes` passed with no model
  changes since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was removed, so no generated code graph update is claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(nonworking-days): preview affected membership impact`.

Next recommended step:

- Implement only the complete Owner-authorized `AddNonWorkingDay` backend
  command: revalidate the expiring exact-scope token in one consistent
  transaction, capture immutable period/application source rows, enforce
  idempotency, synchronously recalculate every affected Membership, append the
  business audit event and roll back all state on any failure. Keep
  CorrectNonWorkingDay, UI and profile/history presentation for later bounded
  steps.

## Step 124 - Owner AddNonWorkingDay backend command

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Implement the roadmap's Owner-only `AddNonWorkingDay` command after the exact
  ADR-016 scope, signed confirmation token and read-only impact preview
  foundations from Steps 121-123.
- Recompute and lock the canonical affected scope in the command transaction;
  never trust the preview as source truth and never allow a stale confirmation
  to produce source, derived, audit or idempotency writes.
- Commit the confirmed period/application snapshot, synchronous Memberships
  recalculation and business audit as one atomic workflow. Keep correction,
  owner UI and profile/history presentation outside this bounded step.

Scope:

- Add `AddNonWorkingDayCommand` with the common operational envelope, inclusive
  period, explicit reason code/comment and signed confirmation token. Extend
  the stable command error taxonomy with `PreviewExpired` and
  `AffectedScopeChanged` while preserving existing numeric values.
- Add normalized command validation for idempotency/correlation/device metadata,
  bounded reason values and canonical token shape. The request fingerprint
  binds actor/session, operational metadata, exact normalized preview input and
  the confirmation token.
- Add `AddNonWorkingDayCommandHandler` and DI registration. It requires the
  canonical active Owner account/session, opens one caller-owned
  `RepeatableRead` transaction and reuses the Memberships-owned impact preparer
  to lock lifecycle-active Membership candidates in deterministic id order and
  recompute the exact ADR-016 scope.
- Revalidate the authenticated token against the current exact
  Membership/Client/full-range set. Expiry returns `PreviewExpired`, any
  authenticated input/scope drift returns `AffectedScopeChanged`, and a
  malformed/tampered token returns validation failure; every path rolls back
  without partial writes.
- On confirmation, create one active `non_working_periods` row and one immutable
  `non_working_period_applications` row per exact affected Membership. Every
  application stores the full proposed range plus authenticated preview and
  server confirmation timestamps.
- Recalculate every affected Membership synchronously in deterministic order
  after the source rows are visible in the same transaction. Any missing,
  invalid or mismatched recalculation result rolls back period/application,
  cache/extension, audit and idempotency state.
- Append `non_working_day.added` with period/reason, exact affected identities,
  preview fingerprint/window and requested/succeeded recalculation summary.
  Store the successful idempotency result atomically and replay the original
  period, affected Membership ids and audit id for exact retries.
- Recover exact concurrent retries after PostgreSQL unique/serialization/lock
  conflicts by rolling back the stale snapshot and rereading the committed
  idempotency result; unrelated conflicts return `ConcurrencyConflict`.
- Add no EF model/migration, `CorrectNonWorkingDay`, NonWorkingDay UI,
  profile/history projection or report behavior.

Validation:

- Focused `AddNonWorkingDayCommandContractsTests` passed 4/4 in Release.
- Focused `PostgreSqlAddNonWorkingDayCommandTests` passed 9/9 with no skips
  against local Docker PostgreSQL. Coverage includes exact full-range snapshot,
  canonical recalculation, audit/idempotency persistence, Owner/input/token
  rejection, scope drift, exact expiry, replay, concurrent same-key execution,
  late recalculation/audit rollback and competing Membership lock handling.
- `dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal
  --no-restore` passed.
- Final `DOTNET_BIN=/home/genik/.dotnet/dotnet
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 295 core tests, 35 web
  tests, 414 PostgreSQL/architecture/security infrastructure tests, 37
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was removed, so no generated code graph update is claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(nonworking-days): add confirmed nonworking periods`.

Next recommended step:

- Add only the canonical source-preparation foundation for
  `CorrectNonWorkingDay`: lock and read the original period plus immutable
  application snapshot in deterministic order, distinguish active/canceled/
  corrected states and define replace-versus-cancel preparation outcomes. Keep
  correction writes, recalculation, audit, idempotency and UI for the following
  bounded step.

## Step 125 - CorrectNonWorkingDay canonical source preparation

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Continue the roadmap's Owner-only `CorrectNonWorkingDay` workflow after the
  accepted Add command, but establish the canonical old-source read and lock
  boundary before adding any correction mutation.
- Preserve ADR-016 semantics explicitly: `replace_range` recomputes a new
  confirmed scope, `replace_reason` preserves the exact old application
  snapshot, and `cancel` creates no replacement scope.
- Keep this step read-only. It adds no correction/cancellation write, source
  status update, replacement period, recalculation, audit, idempotency, command
  authorization or UI.

Scope:

- Add stable `NonWorkingDayCorrectionMode` values for `ReplaceRange`,
  `ReplaceReason` and `Cancel`, plus a NonWorkingDays-owned policy that maps
  them to `RecomputeReplacement`, `PreserveConfirmedApplications` and
  `NoReplacement` scope behavior.
- Add immutable correction source contracts for the original period and its
  exact ordered application snapshot. They validate non-empty identities,
  full-period application ranges, preview/confirmation time order, unique
  application and Membership ids, deterministic Membership/application order,
  matching period/application statuses, and cancellation-fact consistency.
- Add `CorrectNonWorkingDaySourcePreparer` and scoped DI registration. It
  requires a caller-owned `RepeatableRead` or `Serializable` transaction,
  locks every old-scope Membership in deterministic id order before locking
  the period, application and cancellation rows, and performs no tracked or
  persistent write.
- Return stable preparation outcomes for prepared active source, not found,
  already canceled, already corrected and inconsistent source. Terminal
  outcomes retain the explainable original snapshot when its state is
  internally consistent.
- Treat a period/application status mismatch, missing or unexpected
  cancellation fact, duplicate Membership snapshot, non-full range, unknown
  status or broken Membership/client association as inconsistent source rather
  than allowing a future command to mutate ambiguous history.
- Add no EF model or migration. The current source schema remains unchanged.

Validation:

- Focused `CorrectNonWorkingDaySourceContractsTests` passed 5/5 in Release.
- Focused `PostgreSqlCorrectNonWorkingDaySourcePreparerTests` passed 6/6 with
  no skips against local Docker PostgreSQL. Coverage proves the isolation
  guard, missing and terminal outcomes, exact deterministic snapshot,
  read-only behavior, DI registration, inconsistent-state rejection and held
  row locks for Membership, period, application and cancellation sources.
- `dotnet format BodyLife.Crm.sln --no-restore --verify-no-changes` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 300 core tests, 35 web
  tests, 420 PostgreSQL/architecture/security infrastructure tests, 37
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was restored exactly to `HEAD`, so no generated code graph update is
  claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(nonworking-days): prepare correction source snapshots`.

Next recommended step:

- Add only the Memberships-owned replacement impact preparation required by
  `replace_range`: calculate canonical eligibility with the selected old
  period excluded, lock all lifecycle-active candidates in deterministic order
  before old-source preparation, and return exact replacement scope/impact
  confirmation material. Keep correction writes, status transitions,
  recalculation persistence, audit, idempotency and UI for a later bounded
  step.

## Step 126 - NonWorkingDay replacement impact preparation

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Continue `CorrectNonWorkingDay` with the ADR-016 `replace_range` calculation
  boundary after Step 125 established the immutable old-source snapshot.
- Keep Memberships as the sole owner of effective-end, extension-union,
  overlap and eligibility calculations. NonWorkingDays exposes only canonical
  application identities needed to exclude the selected old period.
- Preserve one deterministic lock order for future correction orchestration:
  read old application identities without row locks, lock every
  lifecycle-active Membership candidate by id, calculate canonical state with
  the old source excluded, then allow Step 125 old-source preparation.

Scope:

- Add `IMembershipNonWorkingDayReplacementImpactPreparer` and an immutable
  preparation result that binds the replaced period id, exact sorted old
  application ids and the complete replacement affected-scope/impact model.
- Add `IMembershipNonWorkingDayApplicationSourceProvider`. Extend the existing
  PostgreSQL NonWorkingDay extension reader to return the selected period's
  application ids in deterministic order without taking source row locks.
- Extend the existing affected-scope preparer with one internal replacement
  path. It reuses the same `RepeatableRead`/`Serializable` guard, lifecycle-
  active candidate query, deterministic Membership locks, application policy,
  impact estimator and exact full-period scope construction as Add/Preview.
- Extend canonical Membership calculation with a replacement-only exclusion
  set. Filtering matches both stable source type `non_working_period` and exact
  application id, so an equal Guid belonging to a Freeze or another source is
  never removed. Freeze and all unrelated NonWorkingDay sources remain in the
  union and overlap explanation.
- Keep the ordinary canonical calculation path unchanged. Add stable extension
  source-type constants to the existing Membership source-range contract and
  use them in Freeze/NonWorkingDay readers.
- Register the source-provider alias and replacement preparer as scoped
  services, preserving one scoped NonWorkingDay reader instance. Extend the
  Membership formula ownership gate only with the new reviewed public
  contracts.
- Add no EF model/migration, correction preview token/query, command write,
  period/application status transition, replacement source row, recalculation
  persistence, audit, idempotency or UI.

Validation:

- Focused `MembershipNonWorkingDayReplacementImpactPreparationTests` passed
  3/3 in Release.
- Focused affected-scope plus formula-ownership suite passed 9/9 with no skips
  against local Docker PostgreSQL. Coverage proves input/isolation guards,
  exact old application identity capture, source-specific exclusion, changed
  old/new eligibility, preserved Freeze union, full replacement ranges, DI
  identity, all-active-candidate row locks, later Step 125 source preparation
  composition and unchanged source/cache/audit/idempotency state.
- `dotnet format BodyLife.Crm.sln --no-restore --verify-no-changes` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 303 core tests, 35 web
  tests, 422 PostgreSQL/architecture/security infrastructure tests, 37
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was restored exactly to `HEAD`, so no generated code graph update is
  claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured.

Commit:

- `feat(memberships): prepare nonworking replacement impact`.

Next recommended step:

- Add only the mode-specific signed confirmation contract for
  `CorrectNonWorkingDay`: bind period id, correction mode, exact old source and
  application identities, replacement input plus exact new scope for
  `replace_range`, preserved old scope for `replace_reason`, and no replacement
  scope for `cancel`. Keep the Owner preview query, correction writes,
  recalculation persistence, audit, idempotency and UI for later bounded steps.

## Step 127 - CorrectNonWorkingDay signed confirmation contract

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Continue `CorrectNonWorkingDay` after Step 125 established exact old-source
  preparation and Step 126 established Memberships-owned replacement impact.
- Implement the ADR-016 tamper-resistant confirmation boundary before adding
  the Owner preview query or any correction write orchestration.
- Keep the contract mode-specific: `replace_range` binds replacement input and
  exact new scope, `replace_reason` binds the preserved old scope, and `cancel`
  carries no replacement scope.

Scope:

- Add immutable `NonWorkingDayCorrectionConfirmationMaterial` factories for
  `replace_range`, `replace_reason` and `cancel`. Every factory requires an
  active original source and preserves its exact period/application identity.
- Require `replace_range` material to consume Step 126 replacement preparation,
  match the original period id, match replacement input dates and exclude the
  exact sorted original application id set.
- Derive the `replace_reason` confirmed scope directly from the immutable Step
  125 application snapshot, preserving every Membership, Client and full
  applied range. Expose no confirmed replacement scope for `cancel`.
- Add correction confirmation/result, validation and token-service contracts.
  The HMAC fingerprint binds correction mode/scope behavior, full original
  source metadata, ordered application identities and metadata, replacement
  input, and the exact mode-specific confirmed scope.
- Add a dedicated `bodylife-nwd-correction-v1` token prefix and
  `bodylife.nonworking-day-correction.v1` fingerprint schema.
  `AddNonWorkingDay` preview and correction tokens remain cryptographically
  domain-separated even though they use the same configured signing key and
  lifetime.
- Extract the existing HMAC envelope authentication, canonical payload,
  lifetime and fixed-time fingerprint comparison into one internal codec.
  Preserve the existing Add preview prefix/schema and public behavior.
- Register the correction token service as a lazy configured singleton beside
  the existing Add preview token service.
- Add no EF model/migration, correction preview query, command write, source
  status transition, recalculation persistence, audit, idempotency or UI.

Validation:

- Early Release build passed with 0 warnings/errors.
- Focused correction confirmation material tests passed 4/4 in Release.
- Focused correction HMAC/security/DI tests passed 5/5 in Release.
- Combined correction/Add preview token regression plus Memberships ownership
  gate passed 15/15 with no skips.
- `dotnet format BodyLife.Crm.sln --no-restore --verify-no-changes` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 307 core tests, 35 web
  tests, 427 PostgreSQL/architecture/security infrastructure tests, 37
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was restored byte-for-byte to `HEAD`, so no generated code graph
  update is claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked graph change.

Commit:

- `feat(nonworking-days): sign correction confirmations`.

Next recommended step:

- Add only the Owner-only `PreviewCorrectNonWorkingDay` query orchestration.
  For `replace_range`, prepare and lock all active Membership candidates through
  Step 126 before Step 125 old-source preparation; for `replace_reason` and
  `cancel`, preserve the exact old source/scope. Issue the Step 127 token and
  return old/new confirmation material without source writes, recalculation
  persistence, audit, idempotency or UI.

## Step 128 - Owner CorrectNonWorkingDay preview query

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Continue the accepted `CorrectNonWorkingDay` sequence after exact old-source
  preparation, Memberships-owned replacement impact and signed mode-specific
  confirmation material were established in Steps 125-127.
- Implement only the Owner query boundary required before command-side
  revalidation and persistence. Queries remain free of business audit entries.
- Preserve the ADR-016 lock order for `replace_range`: lock every active
  replacement candidate before locking the old period and its immutable
  application snapshot.

Scope:

- Add `PreviewCorrectNonWorkingDayQuery`, stable result/status contracts and a
  `NonWorkingDayCorrectionPreview` that exposes the exact original source,
  mode-specific replacement input/scope, replacement estimates when applicable
  and the signed confirmation.
- Require a canonical active Owner account/session before validating or reading
  correction state. Validate mode-specific input shapes: inclusive dates and a
  replacement reason for `replace_range`, preserved dates and a replacement
  reason for `replace_reason`, and no replacement fields for `cancel`.
- Execute preview preparation in one caller-owned `RepeatableRead` transaction.
  `replace_range` invokes the Step 126 replacement preparer before the Step 125
  source preparer; `replace_reason` and `cancel` prepare only the old source.
- Map missing, canceled, already-corrected and inconsistent source outcomes to
  explicit query failures. Issue the Step 127 HMAC token only for an active,
  internally consistent source and exact mode-specific material.
- Extract one infrastructure mapper for canonical Memberships impact estimates
  so Add and correction previews expose the same display shape without
  duplicating membership formulas.
- Register the query handler as scoped. Add no EF model/migration, source/cache
  writes, recalculation persistence, business audit, idempotency, command
  handler or UI.

Validation:

- Early Release build passed with 0 warnings/errors.
- Focused query contract tests passed 2/2 in Release.
- Focused PostgreSQL/DI correction-preview tests passed 4/4 with no skips against
  the healthy local Docker PostgreSQL service. They prove all three mode shapes,
  canonical Owner authorization, exact old/new scopes, token validation and no
  source/cache/audit/idempotency writes.
- `dotnet format BodyLife.Crm.sln --no-restore --verify-no-changes` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 309 core tests, 35 web
  tests, 430 PostgreSQL/architecture/security infrastructure tests, 37
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was restored byte-for-byte to `HEAD`, so no generated code graph update
  is claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked graph change.

Commit:

- `feat(nonworking-days): preview nonworking corrections`.

Next recommended step:

- Add only the `CorrectNonWorkingDayCommand` application contract: common
  command envelope, period id, mode-specific replacement fields, correction
  reason/comment and Step 128 confirmation token, plus canonical reread targets
  and error taxonomy. Keep source writes, recalculation persistence, audit,
  idempotency orchestration and UI for subsequent bounded steps.

## Step 129 - CorrectNonWorkingDay command contract

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Continue the accepted `CorrectNonWorkingDay` sequence after the Owner preview
  query established exact mode-specific confirmation material in Step 128.
- Define the application command boundary before implementing validation,
  transaction orchestration or persistence, following the same incremental
  pattern used by existing correction/cancellation workflows.
- Keep correction reason/comment in the common `CommandEnvelope`; keep the
  replacement period reason code/comment separate because it describes the new
  NonWorkingDay source rather than why the Owner performed the correction.

Scope:

- Add `CorrectNonWorkingDayCommand` with the common operational envelope,
  original period id, correction mode, nullable mode-specific replacement
  dates/reason fields and the Step 128 confirmation token.
- Preserve nullable raw command input so a later command preparation policy can
  return stable validation errors instead of relying on constructor/model-binding
  exceptions for invalid mode shapes.
- Define stable entity references for the original/replacement period,
  cancellation fact and affected Memberships. Expose the original period as the
  canonical reread target where correction history can remain explainable.
- Cover `replace_range`, `replace_reason` and `cancel` payload shapes, common
  envelope metadata, idempotency key, correction reason/comment, confirmation
  token, canonical success references and documented correction error taxonomy.
- Add no command preparation/validation policy, authorization, handler or DI,
  EF model/migration, source/cache write, token revalidation, recalculation,
  business audit, idempotency persistence or UI.

Validation:

- Early Release build passed with 0 warnings/errors.
- Focused `CorrectNonWorkingDayCommandContractsTests` passed 14/14 in Release.
- `dotnet format BodyLife.Crm.sln --no-restore --verify-no-changes` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 323 core tests, 35 web
  tests, 430 PostgreSQL/architecture/security infrastructure tests, 37
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was restored byte-for-byte to `HEAD`, so no generated code graph update
  is claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked graph change.

Commit:

- `feat(nonworking-days): define correction command contract`.

Next recommended step:

- Add only a pure `CorrectNonWorkingDay` command preparation/validation policy.
  Normalize and validate the common envelope, Owner correction reason,
  idempotency key, occurred time, confirmation token and the three mode-specific
  replacement shapes, returning canonical prepared input and stable command
  errors. Keep authorization lookup, PostgreSQL transactions/source writes,
  token revalidation against current scope, recalculation, audit and UI for
  later bounded steps.

## Step 130 - CorrectNonWorkingDay command preparation

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Continue the accepted `CorrectNonWorkingDay` sequence immediately after the
  raw application command contract from Step 129, before introducing any
  authorization lookup, consistent-snapshot orchestration or source writes.
- Keep validation in the owning NonWorkingDays module and return stable
  `CommandError` values rather than relying on model-binding or constructor
  exceptions for invalid command payloads.
- Preserve the three ADR-016 correction shapes: `replace_range` carries a new
  inclusive range and reason, `replace_reason` carries no synthetic range, and
  `cancel` carries no replacement input.

Scope:

- Add immutable `CorrectNonWorkingDayPreparation` and preparation-result
  contracts. A prepared command exposes the canonical common envelope,
  original period id, exact mode-specific replacement values, confirmation
  token and canonical source/reread entity ids.
- Add a pure `CorrectNonWorkingDayPreparationPolicy` that validates period id,
  correction mode, actor/request envelope shape, idempotency key, entry origin,
  required occurred time, required Owner correction reason/comment and the
  canonical correction confirmation token format.
- Canonicalize request correlation id, device label, idempotency key,
  reason/comment, UTC occurred time and Unicode-normalized replacement reason
  values. Return `ReasonRequired` for missing correction reason/comment and
  `ValidationFailed` with stable field names for other input failures.
- Validate all mode-specific shapes and bounds. Range replacement requires
  valid inclusive dates plus bounded replacement reason values; reason-only
  replacement rejects dates and exposes no synthetic range; cancel rejects all
  non-empty replacement fields.
- Validate actor metadata structurally but deliberately leave canonical active
  Owner account/session authorization to the later database-backed command
  orchestration step.
- Add no handler or DI registration, PostgreSQL transaction/source write, EF
  model/migration, idempotency persistence, current-scope token revalidation,
  recalculation, business audit or UI.

Validation:

- Early Release build passed with 0 warnings/errors.
- Focused `CorrectNonWorkingDayPreparationPolicyTests` passed 11/11 in Release.
- `dotnet format BodyLife.Crm.sln --no-restore --verify-no-changes` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 334 core tests, 35 web
  tests, 430 PostgreSQL/architecture/security infrastructure tests, 37
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was restored byte-for-byte to `HEAD`, so no generated code graph
  update is claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked graph change.

Commit:

- `feat(nonworking-days): prepare correction commands`.

Next recommended step:

- Add only the database-backed `CorrectNonWorkingDay` command revalidation
  preparation. Require a canonical active Owner, open a caller-owned
  `RepeatableRead` transaction, preserve ADR-016 lock order, rebuild the exact
  mode-specific confirmation material from current source/scope and map token
  validation to `PreviewExpired`, `AffectedScopeChanged` or validation errors.
  Keep correction/cancellation source writes, recalculation persistence,
  business audit, idempotency persistence and UI for later bounded steps.

## Step 131 - CorrectNonWorkingDay current-scope revalidation

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Continue the accepted `CorrectNonWorkingDay` sequence after Step 130's pure
  command preparation, adding the database-backed stale-preview boundary before
  any correction source mutation is implemented.
- Require a caller-owned `RepeatableRead` or `Serializable` transaction so the
  future command handler can retain every acquired row lock through source
  transition, Memberships recalculation, audit, idempotency and commit.
- Preserve ADR-016 mode behavior and lock order. Range replacement locks every
  lifecycle-active Membership candidate before the old period/application
  source rows; reason replacement preserves the old confirmed application
  snapshot; cancel creates no replacement scope.

Scope:

- Add `CorrectNonWorkingDayCommandRevalidationPreparer` and its immutable result
  contract. Successful preparation returns the canonical Step 130 command,
  current mode-specific confirmation material, optional range replacement
  impact and authenticated token metadata while the caller transaction remains
  open.
- Revalidate the command actor against the canonical active Owner account and
  session inside the same consistent database snapshot.
- Reuse the Memberships-owned replacement impact preparer before the Step 125
  source preparer for `replace_range`; prepare only the locked original source
  for `replace_reason` and `cancel`.
- Extract one internal correction confirmation-material factory and use it from
  both the Owner preview query and command revalidation so token fingerprints
  cannot drift through duplicate mode composition logic.
- Map missing, canceled, already-corrected and inconsistent canonical sources
  to stable command errors. Map authenticated expiry to `PreviewExpired`,
  authenticated material/scope drift to `AffectedScopeChanged`, and malformed
  or tampered tokens to `ValidationFailed`.
- Register the revalidation preparer as scoped. Add no command handler, source
  status/replacement/cancellation writes, `SaveChanges`, transaction commit,
  EF model/migration, recalculation persistence, business audit, idempotency
  persistence or UI.
- Extend the PostgreSQL fixture with one replacement-only Membership and prove
  it is locked with the old source rows. Cover caller transaction isolation,
  active Owner authorization, all three correction modes, exact current scope,
  invalid/expired/mismatched tokens, missing source and unchanged source/cache/
  audit/idempotency row counts.

Validation:

- Early Release build passed with 0 warnings/errors.
- Focused configured PostgreSQL correction preparation/revalidation tests passed
  10/10 with no skips against the healthy local Docker PostgreSQL service.
- Focused correction preview plus HMAC correction-token regression passed 15/15
  with no skips.
- `dotnet format BodyLife.Crm.sln --no-restore --verify-no-changes` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 334 core tests, 35 web
  tests, 434 PostgreSQL/architecture/security infrastructure tests, 37
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was restored byte-for-byte to `HEAD`, so no generated code graph
  update is claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked graph change.

Commit:

- `feat(nonworking-days): revalidate correction commands`.

Next recommended step:

- Implement only the complete Owner-authorized `CorrectNonWorkingDay` backend
  command around Step 131's open-transaction revalidation: exact idempotency
  replay, mode-specific retained source transitions and replacement/cancellation
  facts, synchronous recalculation of the old/new Membership union, one
  append-only business audit event and atomic success/rollback. Keep Owner UI,
  profile/history presentation and report changes for later bounded steps.

## Step 132 - Owner CorrectNonWorkingDay backend command

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Complete the roadmap's Owner-only `CorrectNonWorkingDay` backend workflow
  immediately after Steps 125-131 established source locking, replacement
  impact, correction preview/token, command preparation and current-scope
  revalidation.
- Preserve ADR-016 mode semantics: `replace_reason` keeps the exact old
  Membership/Client/full-range snapshot, `replace_range` persists the newly
  confirmed scope with the old source excluded, and `cancel` creates no
  replacement scope.
- Keep source transition, old/new Membership union recalculation, one business
  audit entry and the idempotency outcome inside one `RepeatableRead`
  transaction. Leave Razor/htmx Owner UI, profile/history presentation and
  report changes for later bounded steps.

Scope:

- Add `CorrectNonWorkingDayCommandHandler` and scoped command-handler
  registration. The handler rejects non-Owner actor shapes, applies the pure
  Step 130 preparation policy, verifies the canonical active Owner before
  replay, and delegates locked source/scope/token revalidation to Step 131.
- Add a correction-specific request fingerprint and successful idempotency
  contract. Exact retries return the original replacement period or
  cancellation, original-period reread target, affected Membership union and
  audit id; a changed or incomplete payload returns `DuplicateSubmission`.
  Concurrent same-key serialization/unique races roll back the stale snapshot
  and recover the committed result.
- For `replace_range`, retain the old period/applications as `corrected`, create
  a new active period and exact newly confirmed applications, and use the
  authenticated token issue time as `previewed_at`. For `replace_reason`, do
  the same while preserving every old Membership/Client/applied-range tuple.
- For `cancel`, retain the old period/applications as `canceled` and create one
  explicit `non_working_period_cancellations` source fact with the normalized
  Owner reason. No hard delete or direct derived-state edit is introduced.
- Persist source transitions before calling the Memberships public
  recalculator, then synchronously rebuild the deterministic distinct union of
  old and replacement Membership ids. Inactive explanation rows remain
  rebuildable history while only active replacement sources contribute to the
  aggregate extension/effective-end state.
- Append exactly one `non_working_day.corrected` or
  `non_working_day.canceled` audit entry against the original period. Include
  old/new period and application summaries, preview fingerprint/window,
  old/new/union counts, affected identities and requested/succeeded
  recalculation details.
- Add no EF model or migration. The accepted schema already models replacement
  as a new period/application snapshot and cancellation as its explicit source
  fact; the original/replacement relationship remains Owner-readable through
  retained facts and business audit.
- Add nine focused PostgreSQL tests covering range replacement, reason-only
  snapshot preservation, cancellation, active/inactive extension explanation
  rows, exact replay and changed-payload rejection, token expiry/scope drift,
  recalculation rollback, audit rollback, concurrent same-key recovery and DI
  registration.

Validation:

- Early and post-test Release builds passed with 0 warnings/errors.
- Focused `PostgreSqlCorrectNonWorkingDayCommandTests` passed 9/9 with no skips
  against the healthy local Docker PostgreSQL service.
- `dotnet format BodyLife.Crm.sln --no-restore --verify-no-changes` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 334 core tests, 35 web
  tests, 443 PostgreSQL/architecture/security infrastructure tests, 37
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was restored byte-for-byte to `HEAD`, so no generated code graph
  update is claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked graph change.

Commit:

- `feat(nonworking-days): implement correction workflow`.

Next recommended step:

- Add only the Milestone 8 backend profile projection for extension
  explanations. Extend canonical client-profile Membership rows with ordered
  active/inactive Freeze and NonWorkingDay source explanations from retained
  facts, including range, source status and reason labels, with focused query
  and PostgreSQL tests. Keep Razor rendering, Owner NonWorkingDay management UI,
  reports and general audit/history screens for later bounded steps.

## Step 133 - Client-profile extension explanation projection

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Continue the roadmap's `profile/history extension explanation rows` task
  immediately after the complete Freeze and NonWorkingDay retained-source
  workflows. This bounded step adds the backend profile projection only.
- Preserve ADR-004 ownership: Freezes and NonWorkingDays remain owners of their
  canonical source facts, while Memberships exposes the public explanation read
  contract and Clients/Search only composes that contract into the profile.
- Keep Razor/htmx rendering, Add/CancelFreeze UI, Owner NonWorkingDay management,
  reports and the Milestone 10 general client/audit history screens outside this
  step.

Scope:

- Add the public Memberships
  `GetClientMembershipExtensionExplanations` query/result contract with an
  immutable collection of source-kind, source/application identity, optional
  NonWorkingDay period identity, inclusive range, effective source status and
  normalized reason label.
- Add the PostgreSQL query handler and scoped DI registration. It reads retained
  `freezes` and `non_working_period_applications` joined to their canonical
  periods, never derives extension totals or effective end dates, and returns a
  deterministic active-first, newest-range-first order per Membership.
- Treat a NonWorkingDay explanation as active only when both its period and
  application are active. Any corrected component produces `Corrected`;
  otherwise any canceled component produces `Canceled`, matching the existing
  Memberships source-reader semantics.
- Return only active source rows for an ordinary profile read. When
  `IncludeHistory` is requested, return both active and retained inactive rows so
  canceled/corrected Freeze and NonWorkingDay reasons remain explainable.
- Extend each canonical `ClientMembershipSummary` row with its read-only source
  explanations. `GetClientProfile` verifies client/membership ownership and
  unique source identities, maps query failures without partial profile data,
  and passes `IncludeHistory` through as the inactive-source switch.
- Approve only the new public query/read-model types in the Membership formula
  ownership architecture gate. No formula implementation, cross-module write,
  EF model/migration, Razor view, report or audit-history query was added.
- Add pure projection coverage plus focused PostgreSQL query/profile tests for
  ordering, source identities, ranges, reason labels, active/inactive filtering,
  independent period/application inactive status, authorization, validation,
  not-found behavior, DI registration, immutable collections and atomic profile
  failure mapping.

Validation:

- Early and post-fix Release builds passed with 0 warnings/errors.
- Focused `ClientProfileMembershipProjectionTests` passed 6/6.
- Focused extension query/profile PostgreSQL tests initially passed 19/19; after
  reviewing independent NonWorkingDay component statuses, the final focused
  architecture/query/profile suite passed 21/21 with no skips.
- The first full gate correctly rejected the unreviewed public Memberships
  contracts through `MembershipFormulaOwnershipTests`; after adding only those
  read-contract types to the allowlist, the final gate passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 335 core tests, 35 web
  tests, 449 PostgreSQL/architecture/security infrastructure tests, 37
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local rebuild
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was restored byte-for-byte to `HEAD`, so no generated code graph update
  is claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked graph change.

Commit:

- `feat(memberships): expose profile extension explanations`.

Next recommended step:

- Render only the Step 133 extension explanation rows in the reception client
  profile Membership timeline/panel. Show source kind, inclusive range,
  active/canceled/corrected status and reason label in tablet-first and
  phone-safe Razor/htmx output, with focused web and Playwright coverage. Keep
  Add/CancelFreeze forms, Owner NonWorkingDay management, reports and general
  audit/history screens for later bounded steps.

## Step 134 - Reception profile extension history presentation

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Complete the roadmap's `profile/history extension explanation rows` task by
  rendering the canonical Step 133 read model in the reception client profile.
- Preserve Memberships ownership: Razor maps server-provided source kind,
  inclusive range, status and reason labels only. It does not calculate
  extension days, effective dates or source activity.
- Keep Add/CancelFreeze forms, Owner NonWorkingDay management, reports and the
  Milestone 10 general client/audit history screens outside this bounded step.

Scope:

- Add an `Extension history` section to the existing Membership panel whenever
  the profile timeline contains extension explanations. Group rows by issued
  Membership snapshot and retain active, canceled and corrected source rows.
- Render each Freeze or NonWorkingDay source with its inclusive date range,
  normalized reason label and semantic status chip. Stable source-kind,
  source-status and source-id data attributes support focused browser checks.
- Add compact divider-based styling inside the existing Membership panel, with
  active/canceled/corrected status colors, long-label wrapping and single-column
  phone metadata. No nested card or hover-only interaction was introduced.
- Extend the PostgreSQL UI fixture with one canonical Membership containing an
  active and canceled Freeze plus an active and corrected NonWorkingDay
  application. Rebuild its cache through the real Freeze and NonWorkingDay
  extension providers.
- Add a tablet/phone Playwright scenario that verifies source kinds, all three
  statuses, reason labels, four inclusive ranges and horizontal viewport fit.
  Full-page screenshots were visually reviewed at 1024x768 and 390x844.
- Add no command/action, EF model, migration, report query or audit behavior.

Validation:

- Release solution build passed with 0 warnings/errors.
- The first focused Playwright run reached the new section but exposed an
  ambiguous case-insensitive heading locator; exact accessible-name matching
  fixed the test without changing production behavior.
- Final focused extension-history Playwright coverage passed 2/2 with no skips
  against the healthy local Docker PostgreSQL service.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 335 core tests, 35 web
  tests, 449 PostgreSQL/architecture/security infrastructure tests, 39
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local watcher
  stopped with `Errno 95: Operation not supported`; its partial cache-index
  change is excluded from this step, so no generated code graph update is
  claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked semantic graph update.

Commit:

- `feat(reception): render membership extension history`.

Next recommended step:

- Add only the reception `AddFreeze` Razor/htmx action around the existing
  command: explicit eligible Membership selection, inclusive start/end dates,
  reason, busy/duplicate-submit protection, server validation and canonical
  profile reread, with tablet/phone Playwright coverage. Keep CancelFreeze and
  Owner NonWorkingDay management UI for separate later bounded steps.

## Step 135 - Reception AddFreeze workflow

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Continue the roadmap's `UI for add/cancel freeze` task with only the reception
  AddFreeze half. The existing Milestone 8 backend command remains the sole
  owner of authorization, idempotency, range eligibility, Visit conflict,
  recalculation, source facts and business audit.
- Preserve Memberships ownership: the form shows server-projected Membership
  status/effective end and submits dates, but never predicts extension days or
  a new effective end. Success always rereads the canonical Client profile.
- Keep CancelFreeze and Owner NonWorkingDay management UI outside this bounded
  step.

Scope:

- Add the public `freezes.add` action key and project its Admin/Owner permission
  through `GetClientProfile`, with focused PostgreSQL permission coverage.
- Add a reception AddFreeze view model and Razor partial. The form offers only
  lifecycle-active Membership sources represented by active/expired profile
  rows, keeps selection explicit, accepts inclusive start/end dates, required
  reason and optional comment, and uses a fresh idempotency key.
- Add the Razor Page POST adapter around the existing `AddFreezeCommand`.
  Normal `occurred_at` is server-set; basic input validation prevents invalid
  `DateRange` construction, while canonical range/Visit rules remain in the
  command. Duplicate keys refresh only the form key.
- Map stable permission, not-found, membership eligibility, Visit conflict,
  recalculation and concurrency errors. State-dependent failures refresh the
  entire canonical workspace; local validation keeps the editable form open.
- Use htmx `this:drop`, disabled/busy submit state, scoped loading indicator and
  full-workspace retarget after success. The refreshed Membership panel shows
  the recalculated effective end and new extension explanation row.
- Add isolated tablet/phone PostgreSQL fixtures and a Playwright theory that
  proves invalid range rollback, one source/audit/idempotency success under a
  repeated busy tap, exact source range/reason, two inclusive extension days,
  canonical profile reread, touch target size and horizontal viewport fit.
- Visually review open form/error and canonical success screenshots at 1024x768
  and 390x844. Distinct `Freeze start date`/`Freeze end date` labels avoid
  accessible-name collisions with IssueMembership.
- Add no Freeze/CancelFreeze backend behavior, EF model, migration, report,
  Owner NonWorkingDay UI or general audit/history screen.

Validation:

- Release solution and focused smoke-project builds passed with 0
  warnings/errors.
- Focused `GetClientProfile` permission coverage passed 1/1 against Docker
  PostgreSQL.
- The first AddFreeze Playwright run exposed reserved PostgreSQL test alias
  `freeze`; after changing it to `freeze_row`, focused tablet/phone coverage
  passed 2/2. The final visually polished run also passed 2/2.
- The first full gate passed Core/Web/infrastructure but correctly found one
  existing IssueMembership strict locator made ambiguous by the new generic
  `Start date` label. Distinct Freeze labels fixed the accessibility collision,
  and the combined AddFreeze/IssueMembership regression passed 4/4.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 335 core tests, 35 web
  tests, 449 PostgreSQL/architecture/security infrastructure tests, 41
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local watcher
  stopped with `Errno 95: Operation not supported`; its partial cache-index
  change is excluded from this step, so no generated code graph update is
  claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked semantic graph update.

Commit:

- `feat(reception): add freeze workflow`.

Next recommended step:

- Add only the reception `CancelFreeze` Razor/htmx action on active Freeze
  explanation rows: source identity, explicit destructive confirmation,
  required reason/optional comment, idempotency and busy state, canonical
  profile reread and tablet/phone Playwright coverage. Keep Owner NonWorkingDay
  management UI for later bounded steps.

## Step 136 - Reception CancelFreeze workflow

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Complete the roadmap's reception `UI for add/cancel freeze` task by adding
  only the CancelFreeze half around the existing command. The command remains
  the sole owner of authorization, idempotency, source cancellation,
  recalculation, business audit and changed-after-close evaluation.
- Keep cancellation source-specific and explainable: only active Freeze
  explanation rows expose the action, while the original source remains in the
  canonical Membership timeline after cancellation.
- Preserve Memberships ownership by rereading the full canonical reception
  workspace after success or state-dependent failure. Razor does not calculate
  extension days or the resulting effective end.
- Keep Owner NonWorkingDay preview/add/correct management UI outside this
  bounded step.

Scope:

- Add the public `freezes.cancel` action key and project its Admin/Owner
  permission through `GetClientProfile`, with focused PostgreSQL permission
  coverage.
- Add a cancellation form inside each active Freeze explanation row. It shows
  the canonical Membership, inclusive Freeze range and original reason, and
  requires a cancellation reason plus explicit destructive confirmation;
  comment remains optional.
- Add the Razor Page POST adapter around the existing `CancelFreezeCommand`.
  Normal `occurred_at` is server-set, every rendered form receives a fresh
  idempotency key, and stable permission, validation, not-found, already
  canceled, recalculation and concurrency outcomes are mapped without moving
  business rules into the page.
- Use htmx `this:drop`, disabled/busy submit state and anti-forgery protection.
  Successful submission replaces the complete workspace with canonical state;
  local confirmation/reason errors remain beside the submitted form.
- Add isolated tablet/phone PostgreSQL fixtures and a Playwright theory that
  proves confirmation-bypass rollback, unchanged source/audit/idempotency
  counts after rejection, one committed cancellation under a repeated busy tap,
  exact cancellation reason/comment in source and audit records, source-history
  preservation, two-day effective-end reversal, touch target size and
  horizontal viewport fit.
- Visually review form/error and canonical success screenshots at 1024x768 and
  390x844. Existing AddFreeze and extension-history smoke locators were scoped
  to canonical metadata so the intentionally repeated cancellation context does
  not make them ambiguous.
- Add no CancelFreeze backend behavior, EF model, migration, report query,
  Owner NonWorkingDay UI or general audit/history screen.

Validation:

- Release solution build passed with 0 warnings/errors.
- Focused `GetClientProfile` PostgreSQL coverage passed 15/15.
- The first focused CancelFreeze Playwright invocation used an isolated `HOME`
  without the installed Chromium cache and stopped before app startup. Reusing
  the normal browser cache reached the workflow; one ambiguous reason locator
  was then scoped to canonical metadata, and final tablet/phone cancellation
  coverage passed 2/2.
- The first full gate found four older Playwright reason locators made ambiguous
  by the new source context. After scoping those, the repeat found only two
  AddFreeze inclusive-range locators with the same issue. Stable canonical data
  attributes fixed the assertions, and focused AddFreeze regression passed 2/2
  on the configured Docker PostgreSQL endpoint.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 335 core tests, 35 web
  tests, 449 PostgreSQL/architecture/security infrastructure tests, 43
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local watcher
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was restored to `HEAD`, so no generated code graph update is claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked semantic graph update.

Commit:

- `feat(reception): add freeze cancellation workflow`.

Next recommended step:

- Start the Owner NonWorkingDay UI with only a Razor/htmx
  `PreviewNonWorkingDayImpact` workflow: inclusive range and reason input,
  affected Membership count/list, full applied-range disclosure, overlap
  warnings and expiring confirmation token, with tablet/phone Playwright
  coverage. Keep `AddNonWorkingDay` confirmation and
  `CorrectNonWorkingDay` replace/cancel UI for separate bounded steps.

## Step 137 - Owner NonWorkingDay impact preview UI

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Start the roadmap's Owner NonWorkingDay management UI with only the
  read-only `PreviewNonWorkingDayImpact` workflow. The existing query remains
  the owner of authorization, ADR-016 affected-scope selection, impact
  estimation and expiring confirmation material.
- Preserve Memberships ownership: Razor renders canonical before/after
  effective ends, extension days and overlap sources supplied by the server.
  It does not calculate affected scope or extension effects.
- Keep `AddNonWorkingDay` confirmation and `CorrectNonWorkingDay`
  replace/cancel UI outside this bounded step. Preview requests create no
  source facts, business audit or idempotency records.

Scope:

- Add the Owner-only `/Owner/NonWorkingDays` Razor Page and navigation entry.
  Its anti-forgery-protected htmx POST accepts an inclusive start/end range,
  reason code and optional comment, with server validation and a scoped busy
  state.
- Render the exact affected Membership count and list, full applied range,
  inclusive period days, before/after effective end and extension days,
  unique contribution and existing Freeze/NonWorkingDay overlap warnings.
  Explicit copy communicates ADR-016's full-period endpoint-overlap rule.
- Carry the opaque confirmation token and scope fingerprint in the preview
  response and show their UTC expiry, but expose no confirmation action yet.
- Enrich the shared NonWorkingDay impact preview model with canonical Client
  display names. Both add and correction preview handlers resolve those names
  inside their existing repeatable-read transactions, avoiding UUID-only
  Owner review without changing correction semantics.
- Document the required local signing-key environment variable and its default
  five-minute preview lifetime without committing a secret. The Playwright
  child application receives an isolated test-only key.
- Extend the PostgreSQL UI fixture with a deterministic 2040 period covering
  end-boundary overlap, start-boundary overlap, one existing Freeze overlap
  and one excluded Membership. Add database snapshots proving invalid and
  successful previews do not mutate NonWorkingDay, audit or idempotency data.
- Add Owner tablet/phone Playwright coverage for anti-forgery and htmx wiring,
  invalid-range rollback, exact affected scope, full-period values,
  before/after projections, overlap warning, token/fingerprint/expiry,
  touch-target size and horizontal viewport fit. Add a named-Admin denial
  scenario for both navigation and direct route access.
- Visually review full-page screenshots at 1024x768 and 390x844. The layout is
  readable without overlap, preserves all warnings and actions on phone, and
  introduces no hover-only interaction.
- Add no NonWorkingDay mutation behavior, EF model, migration, report query or
  general audit/history screen.

Validation:

- Release solution builds passed with 0 warnings/errors. The first build
  exposed the existing correction preview's use of the shared mapper; a shared
  Client projection fixed both preview paths without duplicating mapping.
- `dotnet format --verify-no-changes` passed after rerunning outside the
  filesystem sandbox that blocked Roslyn's named pipe.
- Focused PostgreSQL affected-scope coverage passed 10/10 against the healthy
  local Docker PostgreSQL service, including canonical Client display names.
- The first focused Playwright run proved named-Admin denial but found the
  deliberately unconfigured signing key when the new Owner page first resolved
  the token service. Supplying an isolated child-process key and documenting
  local configuration fixed startup without adding a repository secret; final
  focused preview coverage passed 3/3.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 335 core tests, 35 web
  tests, 449 PostgreSQL/architecture/security infrastructure tests, 46
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local watcher
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was restored to `HEAD`, so no generated code graph update is claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked semantic graph update.

Commit:

- `feat(nonworking-days): add owner impact preview UI`.

Next recommended step:

- Add only the Owner `AddNonWorkingDay` confirmation workflow around the
  existing command. It should consume the preview token/fingerprint and exact
  affected scope, require explicit confirmation, use idempotency and a busy
  state, refresh expired or changed previews on `preview_expired` and
  `affected_scope_changed`, and render the canonical result/drill-down on
  tablet and phone. Keep `CorrectNonWorkingDay` UI for a separate bounded
  step.

## Step 138 - Owner AddNonWorkingDay confirmation workflow

Status: completed. Milestone 8 is in progress.

Plan alignment:

- Continue the roadmap's Owner NonWorkingDay management UI with only the
  `AddNonWorkingDay` mutation around the Step 137 impact preview. The existing
  command remains the authority for authorization, confirmation-token
  verification, exact-scope revalidation, transactionality, idempotency,
  recalculation and business audit.
- Preserve ADR-016's immutable confirmed scope and full-period application.
  Razor does not recalculate affected Memberships or extension state; the
  success screen rereads committed source facts and Memberships-owned state.
- Keep `CorrectNonWorkingDay` replace/reason/cancel UI outside this bounded
  step.

Scope:

- Add an Owner-only confirmation form to `/Owner/NonWorkingDays` that carries
  the authenticated preview token, scope fingerprint and a fresh idempotency
  key, requires an explicit exact-scope acknowledgement and uses the shared
  htmx drop/busy behavior to prevent duplicate submissions.
- Refresh the canonical impact preview after validation or command failure.
  Expired previews and changed affected scope receive specific guidance plus
  fresh confirmation material instead of leaving a stale form actionable.
- Add `GetNonWorkingDay` as an explicit public query and PostgreSQL handler for
  the committed period, immutable applications, related Client display names,
  current Membership state and the append-only add audit reference. The Owner
  authorization and source-consistency checks are enforced server-side.
- Validate successful command reread targets against the canonical query,
  render the confirmed period and exact Membership drill-down, push
  `?periodId=...` for htmx navigation and support a direct/reloaded canonical
  result URL.
- Extend the PostgreSQL integration suite for canonical add-query behavior,
  Owner authorization and missing-period handling.
- Add independent tablet and phone Playwright fixtures. Each scenario proves
  required acknowledgement, changed-scope refresh after a concurrent
  Membership interval change, fresh token/fingerprint/idempotency material,
  duplicate-submit protection, one atomic source/audit/idempotency write,
  exact full-period applications and canonical reload.
- Visually review final 1024x768 and 390x844 screenshots. The confirmation
  outcome, warnings and exact-scope drill-down remain readable without
  overlap or horizontal scrolling.
- Add no EF model or migration, correction mutation UI, report query or
  general audit/history screen.

Validation:

- `dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal
  --no-restore` and `git diff --check` passed.
- The focused canonical PostgreSQL query test passed 1/1 against the local
  Docker PostgreSQL service after using the same development-configuration
  connection bridge as the shared validation script.
- The first focused Playwright run exposed a test-only unsupported regex flag;
  removing that locator flag left product behavior unchanged. Final focused
  confirmation coverage passed 2/2 and the complete NonWorkingDay UI class
  passed 5/5.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 335 core tests, 35 web
  tests, 450 PostgreSQL/architecture/security infrastructure tests, 48
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but the local watcher
  stopped with `Errno 1: Operation not permitted`; its partial cache-index
  change was restored to `HEAD`, so no generated code graph update is claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked semantic graph update.

Commit:

- `feat(nonworking-days): add owner confirmation workflow`.

Next recommended step:

- Add only an Owner read-only `CorrectNonWorkingDay` preview workflow around
  the existing preview query: select an active period, choose replace range,
  replace reason or cancel mode, and review old/new exact scopes plus canonical
  impact on tablet and phone. Keep correction confirmation and mutation for a
  separate bounded step.

## Step 139 - Owner CorrectNonWorkingDay impact preview UI

Status: completed. Milestone 8 remains in progress.

Plan alignment:

- Continue the roadmap's Owner NonWorkingDay management UI with only the
  read-only `PreviewCorrectNonWorkingDay` workflow recommended by Step 138.
  The existing query remains the authority for authorization, correction-mode
  rules, canonical old scope, replacement scope, Memberships-owned impact and
  expiring confirmation material.
- Preserve ADR-016's immutable application-scope semantics. Razor renders the
  server-supplied old/new exact scopes and full-period effects; it does not
  select affected Memberships or calculate extension state.
- Keep correction confirmation and the `CorrectNonWorkingDay` mutation outside
  this bounded step. Preview requests create no source facts, business audit or
  idempotency records.

Scope:

- Add an Owner-authorized query and PostgreSQL handler that lists active
  NonWorkingDay periods available for correction, ordered deterministically
  and guarded by canonical active-application count consistency.
- Add a separate correction preview workspace to `/Owner/NonWorkingDays` with
  active-period selection and replace-range, replace-reason and cancel modes.
  The anti-forgery-protected htmx request has scoped busy behavior and a
  full-page fallback.
- Require the future correction reason/comment at the UI boundary while
  sending only mode-relevant replacement fields to the existing read-only
  preview query.
- Canonically reread the selected period after a successful preview and render
  confirmation material only when that source still exactly matches the
  preview's original source and application scope.
- Render old/new exact scope counts and Clients, scope-preservation or
  deactivation behavior, full-period replacement impact, token, fingerprint
  and UTC expiry. No confirmation button or correction command endpoint is
  exposed.
- Extend PostgreSQL integration coverage for active-period listing, canonical
  affected count and named-Admin denial.
- Add independent tablet and phone Playwright fixtures for all three modes,
  validation, htmx/anti-forgery wiring, exact old/new scope, confirmation
  material and proof that previews do not mutate source, audit or idempotency
  data.
- Add no EF model, migration, report query, general audit/history screen or
  correction mutation behavior.

Validation:

- The focused PostgreSQL active-correction-list test passed 1/1 against the
  healthy local Docker PostgreSQL service, including canonical affected count
  and named-Admin denial. An initial invocation without the repository's
  PowerShell connection bridge skipped the test and was not counted.
- Focused Owner correction preview Playwright coverage passed 2/2 for tablet
  and phone, covering all three modes and proving preview requests make no
  source, audit or idempotency writes.
- Full-page 1024x768 and 390x844 screenshots for range replacement and
  cancellation were reviewed. The form, warnings, old/new scopes and
  Membership impact remain readable without overlap or horizontal scrolling.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 335 core tests, 35 web
  tests, 451 PostgreSQL/architecture/security infrastructure tests, 50
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration, and `git diff --check` passed.
- `graphify update .` was attempted after the code change but its watcher could
  not rebuild on this filesystem (`Errno 95: Operation not supported`). Its
  partial cache-index change was restored, so no generated code graph update is
  claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked semantic graph update.

Commit:

- `feat(nonworking-days): add owner correction preview UI`.

Next recommended step:

- Add only the Owner `CorrectNonWorkingDay` confirmation/mutation workflow
  around the existing preview and command contracts. It should consume the
  expiring token and exact old/new scope fingerprint, require explicit Owner
  acknowledgement and correction reason, preserve idempotency, recalculation
  and business audit, refresh stale previews, and canonically reread the
  committed correction on tablet and phone. Keep reports and the general audit
  UI outside that bounded step.

## Step 140 - Owner CorrectNonWorkingDay confirmation workflow

Status: completed. Milestone 8 remains in progress.

Plan alignment:

- Complete the roadmap's Owner-only NonWorkingDay correction UI around the
  existing Step 139 preview and `CorrectNonWorkingDay` command. The command
  remains authoritative for authorization, signed confirmation, exact-scope
  revalidation, idempotency, transactionality, Membership recalculation and
  append-only business audit.
- Preserve ADR-016 correction semantics for replace range, replace reason and
  cancel. Razor renders canonical old/new scopes and Memberships-owned state;
  it does not select affected Memberships or calculate extension values.
- Keep reports and the general audit/history UI outside this bounded step.

Scope:

- Add a public Owner-authorized correction-outcome query and PostgreSQL handler
  that rereads the correction audit, original source, replacement or
  cancellation facts, immutable application rows and current Membership state.
  It rejects mismatched audit linkage, action/mode, old/new/union scope or
  recalculated source status as inconsistent.
- Extract the existing period/application projection into a shared canonical
  reader so add and correction rereads use one PostgreSQL mapping without
  moving any business formula out of Memberships.
- Add the anti-forgery-protected correction confirmation endpoint. It requires
  explicit exact-scope acknowledgement, correction reason/comment, signed
  token, canonical fingerprint shape and a fresh idempotency key, then invokes
  only the existing command contract.
- Validate command entity/audit/reread targets, query the committed canonical
  outcome and require an exact affected-Membership match before rendering
  success. htmx uses drop/busy duplicate-submit protection and pushes a
  reloadable `periodId` plus `correctionAuditId` URL.
- On missing acknowledgement, expired confirmation or changed scope, perform a
  fresh server preview and issue new confirmation material. A stale-scope
  command writes no source, application, audit or idempotency rows.
- Render canonical original and replacement/cancellation facts, exact affected
  Client/Membership rows, current effective end/extension state, timestamps,
  correction reason/comment and audit reference. The canonical result appears
  before the next correction form on tablet and phone.
- Extend PostgreSQL coverage for all successful correction modes, canonical
  outcome reread and named-Admin denial. Extend Playwright fixtures for isolated
  replace-range, replace-reason, cancel and concurrent stale-scope scenarios,
  including required acknowledgement, repeated tap and reload behavior.
- Add no EF model, migration, report query or general audit/history screen.

Validation:

- Release build passed with 0 warnings/errors. `dotnet format BodyLife.Crm.sln
  --verify-no-changes --verbosity minimal --no-restore` and
  `git diff --check` passed.
- Focused PostgreSQL add/query and correction/outcome regression coverage passed
  20/20 against the local Docker PostgreSQL service.
- Focused correction mutation Playwright coverage passed 4/4, and the complete
  `NonWorkingDayPreviewSmokeTests` class passed 11/11.
- Final 1024x768 replace-range, 1024x768 replace-reason, 390x844 cancellation
  and stale-scope refresh screenshots were reviewed. They have no overlap or
  horizontal overflow, and canonical success precedes the next correction
  form.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 335 core tests, 35 web
  tests, 451 PostgreSQL/architecture/security infrastructure tests, 54
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code change but its watcher could
  not rebuild on this filesystem (`Errno 95: Operation not supported`). Its
  generated cache-index change was restored, so no code graph update is
  claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked semantic graph update.

Commit:

- `feat(nonworking-days): add owner correction confirmation`.

Next recommended step:

- Add a bounded Milestone 8 mass-recalculation performance/transaction gate for
  realistic NonWorkingDay affected-Membership counts and define the synchronous
  failure behavior when recalculation cannot complete. The UI must never report
  success unless every source/application, Membership state, audit and
  idempotency write commits atomically.

## Step 141 - NonWorkingDay mass recalculation quality gate

Status: completed. Milestone 8 remains in progress pending its acceptance
closeout audit.

Plan alignment:

- Complete the roadmap's remaining explicit Milestone 8
  performance/transaction test requirement and define the fallback behavior
  when NonWorkingDay mass recalculation is too slow or interrupted.
- Preserve ADR-016 and the interaction contract's v1 synchronous transaction.
  Do not add background jobs, partial batches or a second source-of-truth state.
- Keep Reports, general audit/history UI and production monitoring
  implementation outside this bounded quality/operations step.

Scope:

- Add a PostgreSQL-backed realistic-volume suite using real Add/Correct command
  handlers, signed previews, RepeatableRead transactions and the Memberships
  cache rebuilder. No mock database, SQLite or EF InMemory path is used.
- Set a conservative one-gym regression baseline of 250 simultaneously
  affected active Memberships and a 30-second budget for each confirmed
  `AddNonWorkingDay` and reason-replacement `CorrectNonWorkingDay` command.
- Verify all 250 immutable applications, Membership cache rows and active/
  inactive explanation rows, exact command result targets, complete audit
  recalculation summaries and idempotency records after Add and correction.
- Add a separate 120-Membership correction scenario that cancels the command
  after 25 successful canonical cache/explanation writes. Verify the thrown
  cancellation rolls back original source status, replacement source and
  applications, all newer recalculation timestamps, audit and idempotency, and
  clears the EF change tracker.
- Define provider-neutral v1 fallback policy in `operations-design.md`: stable
  `recalculation_failed` or cancellation means atomic rollback and no UI
  success; canonical state must be reread before retry; async processing would
  require a new ADR with durable pending/failed/retry state.
- Extend command-latency operational review to Add/CorrectNonWorkingDay and
  require the tested volume to be raised before go-live when migration
  inventory exceeds 250 potentially affected Memberships.
- Add no product runtime code, EF model, migration or UI change.

Validation:

- Release solution build passed with 0 warnings/errors.
- `dotnet format BodyLife.Crm.sln --verify-no-changes --verbosity minimal
  --no-restore` and `git diff --check` passed.
- The new focused PostgreSQL mass suite passed 2/2. The 250-Membership Add plus
  correction case completed in approximately 6 seconds total test time, and
  the 120-Membership cancellation/rollback case completed in approximately 6
  seconds.
- Combined Add, Correct and mass NonWorkingDay command regression coverage
  passed 22/22 against the local Docker PostgreSQL service.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 335 core tests, 35 web
  tests, 453 PostgreSQL/architecture/security infrastructure tests, 54
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the test change but its watcher could
  not rebuild on this filesystem (`Errno 95: Operation not supported`). Its
  generated cache-index change was restored, so no code graph update is
  claimed.
- `graphify . --update` was attempted after the documentation changes but
  stopped because no semantic extraction LLM backend is configured; it
  produced no tracked semantic graph update.

Commit:

- `test(nonworking-days): add mass recalculation gate`.

Next recommended step:

- Perform one bounded Milestone 8 acceptance closeout audit against every
  roadmap criterion and required-test row. Record direct code/test evidence,
  resolve only genuine gaps, and mark Milestone 8 complete before beginning the
  Milestone 9 Reports sequence.

## Step 142 - Milestone 8 acceptance closeout

Status: completed. Milestone 8 is complete.

Plan alignment:

- Audit every Milestone 8 acceptance criterion and required-test row in
  `implementation-roadmap.md` against direct implementation and automated-test
  evidence before starting Reports.
- Resolve only a demonstrated closeout gap. Do not add Report behavior, alter
  accepted Freeze/NonWorkingDay policy, or expand the product surface during
  the audit.
- Preserve the existing synchronous command, Memberships formula ownership,
  retained-source history and PostgreSQL transaction boundaries.

Closeout result:

- All Milestone 8 schema, command, recalculation, profile explanation, UI,
  audit and fallback tasks have direct implementation and regression evidence.
- The audit found one missing test row: no test ran real `AddFreeze` and
  `MarkVisit` commands against the same Membership while both transactions were
  queued on the canonical PostgreSQL Membership lock.
- Add two deterministic PostgreSQL races to
  `PostgreSqlMarkVisitCommandTests`. A third transaction holds the Membership
  row while backend wait state proves the first and second commands are queued
  in the intended order.
- With AddFreeze queued first, Add commits one active Freeze and canonical
  one-day extension; MarkVisit then returns `visit_during_freeze` with no Visit,
  consumption, audit or idempotency success.
- With MarkVisit queued first, Mark commits one counted Visit; AddFreeze then
  returns `freeze_conflicts_with_visit` with no Freeze, audit or idempotency
  success.
- Both orderings retain exactly one successful business audit/idempotency row
  and one consistent Membership cache. The existing runtime lock order already
  provides the required serialization, so no product code, EF model, migration
  or UI change is needed.

Acceptance-criterion evidence:

| Roadmap criterion | Direct evidence |
|---|---|
| Freeze ranges are inclusive and effective end changes only through Memberships recalculation. | `MembershipExtensionCalculatorTests.SingleActiveRangeExpandsBothInclusiveEdges`, `MembershipStateExtensionCalculationTests.CanonicalUnionUpdatesOnlyExtensionOwnedState` and `PostgreSqlAddFreezeCommandTests.SuccessfulFreezeCommitsUnionStateAuditAndIdempotency`. |
| Freeze start is bounded by Membership start and locked pre-command effective end; eligible end is not clipped. | `MembershipFreezeEligibilityPolicyTests.InclusiveStartBoundsAreEligibleAndEndIsNotClipped`, its before-start/after-end cases, and `PostgreSqlMembershipStateCacheRebuildTests.CanonicalStateDrivesInclusiveFreezeRangePreparation`. |
| Active counted Visit overlap is rejected while canceled and one-off/trial Visits do not block. | `MembershipFreezeEligibilityPolicyTests.ActiveCountedVisitOnEitherInclusiveEndpointBlocksFreeze`, `CanceledAndOutsideVisitsDoNotBlockFreeze`, `PostgreSqlAddFreezeCommandTests.EligibilityAndVisitConflictsFailWithoutFreezeMutation`, and the two new cross-command races. One-off/trial commands create no Membership consumption in `PostgreSqlMarkVisitCommandTests.OneOffAndTrialWriteOnlyVisitAndAuditFacts`. |
| CancelFreeze preserves history and removes only its active extension contribution. | `PostgreSqlCancelFreezeCommandTests.SuccessfulCancellationCommitsHistoryStateAuditAndIdempotency`, `CancelingOverlappedFreezeKeepsRemainingActiveUnion`, and `PostgreSqlFreezesStorageTests.CancellationIsUniqueAndSourceHistoryUsesRestrictiveDeletes`. |
| NonWorkingDay add/correction is Owner-only and requires preview plus exact-scope confirmation. | `PostgreSqlNonWorkingDayAffectedScopePreparerTests.OwnerPreviewReturnsExactImpactAndBoundTokenWithoutWrites`, `PostgreSqlAddNonWorkingDayCommandTests.OwnerInputAndTokenFailuresDoNotWriteCommandState`, correction command/token suites, and `NonWorkingDayPreviewSmokeTests.NamedAdminCannotNavigateToOrOpenNonWorkingDayPreview`. |
| NonWorkingDay scope uses lifecycle-active Memberships with canonical inclusive overlap calculated without the proposed/replaced period. | `MembershipNonWorkingDayApplicationPolicyTests`, including lifecycle, endpoint and proposed-source exclusion cases, plus `PostgreSqlNonWorkingDayAffectedScopePreparerTests.PreparationReturnsExactCanonicalScopeWithoutWrites` and `ReplacementPreparationExcludesOnlyOldPeriodAndLocksAllCandidates`. |
| Every confirmed NonWorkingDay application contributes its full inclusive period without Membership-boundary clipping. | `MembershipNonWorkingDayApplicationPolicyTests.PeriodEndingOnMembershipStartAppliesTheFullRange`, `PeriodStartingOnEffectiveEndAppliesTheFullRange`, and `PostgreSqlNonWorkingDaysStorageTests.ApplicationsRequireFullPeriodRangeAndMatchingMembershipClient`. |
| Freeze and NonWorkingDay overlap counts the union of unique calendar dates. | `MembershipExtensionCalculatorTests.OverlappingActiveSourcesCountUnionAndPreserveEveryAttribution`, `MembershipNonWorkingDayImpactEstimatorTests.ActiveFreezeAndNonWorkingOverlapUseUniqueDaysAndDeterministicWarnings`, and `PostgreSqlNonWorkingDayExtensionSourceTests.RebuildUnionsFreezeAndNonWorkingDaysAndRetainsInactiveExplanations`. |
| Confirmed NonWorkingDay application scope is immutable and remains explainable after later source changes. | `PostgreSqlAddNonWorkingDayCommandTests.SuccessfulCommandCommitsExactSnapshotStateAuditAndIdempotency`, `PostgreSqlCorrectNonWorkingDayCommandTests.ReplaceReasonPreservesExactConfirmedMembershipSnapshot`, storage lifecycle tests and the retained profile explanation query. |
| Correct/cancel NonWorkingDay recalculates the old/new affected union. | `PostgreSqlCorrectNonWorkingDayCommandTests.ReplaceRangeCommitsNewScopeAndRecalculatesOldNewUnion`, `CancelRetainsCanceledSourcesAndCreatesCancellationFact`, and the reason-replacement snapshot case. |
| Client profile shows active and retained Freeze/NonWorkingDay reasons and history. | `PostgreSqlGetClientMembershipExtensionExplanationsQueryTests`, client-profile projection tests and `ReceptionDashboardSmokeTests.MembershipExtensionHistoryRendersCanonicalSourcesOnTargetViewport` on tablet and phone. |
| Recalculation and business audit commit consistently; any failure blocks success. | Add/CancelFreeze and Add/CorrectNonWorkingDay recalculation/audit rollback tests, `PostgreSqlNonWorkingDayMassRecalculationTests.CancellationAfterPartialMassRecalculationRollsBackEverything`, and both new Visit/Freeze races. |

Required-test evidence:

| Roadmap test row | Direct evidence |
|---|---|
| Domain rules | `MembershipFreezeEligibilityPolicyTests`, `MembershipExtensionCalculatorTests`, `MembershipStateExtensionCalculationTests`, `MembershipNonWorkingDayApplicationPolicyTests`, `MembershipNonWorkingDayImpactEstimatorTests` and replacement-impact tests cover inclusive ranges, eligibility, endpoints, source exclusion and union behavior. |
| Add/CancelFreeze application behavior | `PostgreSqlAddFreezeCommandTests`, `PostgreSqlCancelFreezeCommandTests`, Membership Freeze preparation cases and the new real-command races cover permissions, validation, Membership-first source locking, Visit concurrency, idempotency, audit and rollback. |
| Preview/Add/CorrectNonWorkingDay application behavior | Preview/correction token security suites, affected-scope preparation, `PostgreSqlAddNonWorkingDayCommandTests` and `PostgreSqlCorrectNonWorkingDayCommandTests` cover Owner policy, fingerprints, expiry, stale scope, immutable applications, overlap warnings, old/new scope and recalculation. |
| PostgreSQL constraints and application rows | `PostgreSqlFreezesStorageTests`, `PostgreSqlNonWorkingDaysStorageTests`, extension-day storage/writer tests and migration checks run on PostgreSQL rather than an in-memory substitute. |
| Realistic performance and transaction behavior | `PostgreSqlNonWorkingDayMassRecalculationTests` proves the 250-Membership synchronous Add/Correct budget and atomic rollback after partial work in a 120-Membership cancellation case. |
| Tablet/phone UI | `AddFreezeSmokeTests`, `CancelFreezeSmokeTests`, `NonWorkingDayPreviewSmokeTests` and the profile extension-history smoke cover add, cancel, preview, confirm, every correction mode, stale-scope refresh, duplicate-submit protection, canonical reread and Owner-only access. |

Validation:

- The two new real PostgreSQL Visit/Freeze race tests passed 2/2 with no skips
  against the healthy local Docker PostgreSQL service.
- Focused MarkVisit, AddFreeze and CancelFreeze command coverage passed 36/36.
- `dotnet format BodyLife.Crm.sln --no-restore --verify-no-changes --verbosity
  minimal` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 335 core tests, 35 web
  tests, 455 PostgreSQL/architecture/security infrastructure tests, 54
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the test change but its watcher could
  not rebuild on this filesystem (`Errno 95: Operation not supported`). The
  generated cache-index change was restored, so no code graph update is
  claimed.
- `graphify . --update` was attempted after the progress documentation change
  but stopped because no semantic extraction LLM backend is configured; it
  produced no tracked semantic graph update.

Commit:

- `test(freezes): close milestone 8 acceptance`.

Next recommended step:

- Start Milestone 9 with one bounded backend `GenerateDailyReport` contract and
  composition step over the existing canonical daily Visit and Payment source
  snapshots. Derive totals from returned rows, retain correction/cancellation
  drill-downs and keep report UI plus threshold Membership reports for later
  independent steps.

## Step 143 - Milestone 9 daily report backend composition

Status: completed. Milestone 9 is in progress.

Plan alignment:

- Start the roadmap's Milestone 9 with the bounded backend
  `GenerateDailyReport` contract identified by the Milestone 8 closeout. Do not
  add report UI, threshold Membership reports, long-period accounting or a
  report cache in this step.
- Compose the existing Visits and Payments public daily source queries instead
  of duplicating their PostgreSQL projections or reading business audit as a
  source of totals.
- Keep every embedded total tied to the same canonical rows, retain canceled
  and replaced source facts for explanation, and fail without a partial report
  when either source query or their shared date/day status is inconsistent.

Scope:

- Add `GenerateDailyReportQuery`, result/status contracts and a Reports-owned
  immutable `DailyReportSnapshot` with business date, day reconciliation
  status, active Visit count, active Payment count, daily cash sum and optional
  Visit/Payment drill-down rows.
- Derive totals from the complete returned source rows before applying the
  optional drill-down projection. Default report requests include drill-down;
  summary mode keeps the canonical totals and explicitly returns
  `DrillDownIncluded = false` with empty embedded row collections.
- Expose retained canceled Visit rows, canceled Payment rows and both original
  and replacement sides of Payment corrections as filtered report collections.
- Reject mismatched business dates, mismatched Visit/Payment reconciliation
  statuses, unknown canonical row statuses and mixed active Payment currencies
  as `source_inconsistent` rather than returning understated totals.
- Add a sequential composite query handler and scoped DI registration. The
  handler stops after a failed Visit source query, maps permission/validation
  failures, and never returns a partial authoritative report.
- Keep day-close change markers and audit/history navigation for a later step:
  no canonical close timestamp/source fact currently exists, so this step
  surfaces the existing day status without fabricating changed-after-close
  labels.
- Add no EF model, migration, write path or query-time business audit entry.

Validation:

- Focused core report contract coverage passed 5/5, including defensive
  drill-down storage, canonical total derivation, summary mode, source
  inconsistency checks and failure shapes.
- Focused PostgreSQL report handler coverage passed 4/4 with no skips against
  the healthy local Docker PostgreSQL service. The canonical scenario proves
  one active Visit, one active Payment and a `900 UAH` cash sum while retaining
  Visit/Payment cancellations and both Payment correction rows.
- Combined `GenerateDailyReport`, daily Visit source and daily Payment source
  regression coverage passed 17/17, including the existing corrected-payment
  cross-date explanation and daily query-index cases.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed:
  Release build 0 warnings/errors, formatting/analyzers, 340 core tests, 35 web
  tests, 459 PostgreSQL/architecture/security infrastructure tests, 54
  Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code changes but its watcher
  could not rebuild on this filesystem (`Errno 95: Operation not supported`).
  Its generated cache-index change was restored, so no code graph update is
  claimed.
- `graphify . --update` was attempted after this progress update but stopped
  because no semantic extraction LLM backend is configured; it produced no
  tracked semantic graph update.

Commit:

- `feat(reports): compose canonical daily report`.

Next recommended step:

- Add one bounded `ListEndingSoonMemberships` backend query for Milestone 9
  with the default seven-day threshold, reading Memberships-owned canonical
  state/effective end dates and reviewing the PostgreSQL query path before any
  report UI work.

## Step 144 - Milestone 9 ending-soon Membership report backend

Status: completed. Milestone 9 is in progress.

Plan alignment:

- Implement only the roadmap's bounded `ListEndingSoonMemberships` backend
  query. Keep low-remaining, negative, inactive and report UI work for later
  independent steps.
- Read lifecycle and calculated values through a reviewed Memberships public
  query contract. Reports must not calculate effective end, remaining visits,
  warnings or extension state independently.
- Use the existing PostgreSQL effective-end query path and add no schema,
  migration, write workflow or business-audit side effect.

Scope:

- Add `GetEndingSoonMembershipStateRowsQuery` and immutable Memberships source
  page/row/result contracts for a requested as-of date, day threshold and
  bounded offset pagination.
- Add a PostgreSQL Memberships query handler that authorizes the canonical
  Owner/named Admin/shared Reception session, filters lifecycle-active
  Memberships by inclusive cached `effective_end_date` range, and orders rows
  by effective end, normalized client name and Membership id.
- Fail the complete query as `recalculation_failed` when any possible
  lifecycle-active candidate has a missing or stale Membership state cache.
  Reconstruct every visible `MembershipStateReadModel` through the existing
  Memberships factory, including canonical warnings and extension explanation.
- Add `ListEndingSoonMembershipsQuery` and a Reports-owned page projection.
  Reports retains the exact Membership state object and computes only the
  requested `days_left = effective_end_date - as_of_date` selector value.
- Default the report to seven days through the approved Memberships query
  contract, enforce limit/offset bounds, return stable next offsets and reject
  mismatched or impossible source pagination without partial rows.
- Register both source and composite handlers as scoped services and explicitly
  review the five new Memberships DTO/query/result types in the architecture
  contract allowlist. Formula implementations remain forbidden outside the
  Memberships module.
- Reuse `ix_membership_state_cache_effective_end_date`; no EF model or migration
  change is required.

Validation:

- Focused core contract coverage passed 5/5 for query defaults, canonical state
  retention, the sole Reports-owned days-left calculation, defensive pages,
  selector consistency and failure shapes.
- Focused PostgreSQL report coverage passed 5/5 with no skips against the
  healthy local Docker PostgreSQL service. It covers inclusive 0/3/7-day
  selection, exclusion of expired/canceled/eight-day rows, deterministic name
  ordering, threshold filtering, pagination, profile/report state agreement,
  permissions, invalid selectors, missing/stale cache failure, DI and source
  failure mapping.
- The PostgreSQL `EXPLAIN (COSTS OFF)` assertion confirms the ending-soon range
  path uses `ix_membership_state_cache_effective_end_date`.
- The focused Membership formula ownership architecture gate passed 2/2 after
  reviewing only the new public source contracts.
- `dotnet format BodyLife.Crm.sln --verify-no-changes --no-restore` passed.
- Final `CONFIGURATION=Release DOTNET_ROOT=/home/genik/.dotnet
  DOTNET_BIN=/home/genik/.dotnet/dotnet
  DOTNET_CLI_HOME=/tmp/bodylife-dotnet-home
  NUGET_PACKAGES=/home/genik/.nuget/packages
  BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 ./scripts/validate.sh` passed with
  exit code 0: Release build 0 warnings/errors, formatting/analyzers, 345 core
  tests, 35 web tests, 464 PostgreSQL/architecture/security infrastructure
  tests, 54 Playwright smoke tests and EF migration listing through
  `20260717072704_AddNonWorkingDaySourceFacts`.
- `dotnet-ef migrations has-pending-model-changes` passed with no model changes
  since the latest migration.
- `graphify update .` was attempted after the code changes but its watcher
  could not rebuild on this filesystem (`Errno 95: Operation not supported`).
  Its generated cache-index change was restored, so no code graph update is
  claimed.
- `graphify . --update` was attempted after this progress update but stopped
  because no semantic extraction LLM backend is configured; it produced no
  tracked semantic graph update.

Commit:

- `feat(reports): list memberships ending soon`.

Next recommended step:

- Add one bounded `ListLowRemainingMemberships` backend query for Milestone 9
  with the default `remaining_visits <= 2` threshold, consuming
  Memberships-owned canonical state and reviewing the existing PostgreSQL
  remaining-visits index before any report UI work.
