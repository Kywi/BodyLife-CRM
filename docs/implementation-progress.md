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
