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
