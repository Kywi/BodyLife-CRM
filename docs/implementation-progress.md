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
