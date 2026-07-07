# BodyLife CRM v1 technology stack decision

Дата: 2026-07-07
Статус: application stack selected; hosting provider pending
Основа: `docs/architecture-baseline.md`, `docs/domain-model.md`, `docs/data-architecture.md`, `docs/interaction-contracts.md`, `docs/operations-design.md`, accepted ADR package у `docs/adr/`.

Цей документ фіксує обраний application stack для реалізації BodyLife CRM v1:

```text
ASP.NET Core 10 LTS + Razor Pages/MVC + htmx + EF Core/Npgsql + PostgreSQL
```

Рішення покриває backend language/framework, UI approach, database, ORM/migrations, logging/error handling і testing approach. Hosting provider лишається окремим conditional decision: production deployment має довести backup/restore policy не гірше ADR-009, включно з minimum 30-day backup retention expectation і restore rehearsal before production use.

## 1. Decision drivers

### ADR drivers

- Hosted internal web app для одного залу, без offline-first, native mobile app, SaaS/multi-tenant scope, public client portal або online payments у v1. Стек має запускати один internal web app, а не platform/API ecosystem.
- Modular monolith: один application deploy, одна transactional system, module boundaries навколо Clients/Search, MembershipTypes, Memberships, Visits, Payments, Freezes, NonWorkingDays, Reports, Audit, Users/Roles.
- Hybrid server-rendered UI: сервер рендерить dashboard, forms, client profile, reports і settings; interactive islands тільки для reception-critical зон.
- Server-side commands/actions: усі state changes проходять через application command, authorization, transaction boundary, idempotency/duplicate-submit guard, membership recalculation, business audit і canonical reread.
- Relational consistency: потрібні ACID transactions, row-level locking, foreign keys, checks, transactional unique constraints, partial/filtered unique indexes або еквіваленти.
- Business audit окремо від technical logs: append-only audit table/model має бути частиною command workflow, а не логуванням.
- Reports як query/report layer: live reports читають canonical source facts і Memberships queries/read models; кожен total має drill-down.
- Backup/restore: production-ready означає managed automated backups, minimum 30-day retention expectation, documented restore runbook і restore rehearsal before production use.
- Paper fallback: outage records заходять назад через ordinary domain commands з `occurred_at`, `recorded_at`, `entry_origin = paper_fallback`, reason/comment і audit.
- Low operational complexity: v1 не повинен тягнути microservices, broker, distributed workflows, separate public API, offline sync або складний frontend state.

### Domain drivers

- Reception speed: exact card search, phone/name search, compact client profile, one-tap visit/payment workflows, tablet/phone usability.
- Correct membership calculations: inclusive dates, remaining visits, negative visits, first negative visit date, freeze/non-working union days, cancellation and backdated recalculation.
- Cash reporting: daily cash/visits report must reconcile with payment/visit source rows and corrections.
- Auditability: owner must understand who did what, under which account/session, when it occurred, when it was recorded, and why.
- Migration from paper/Excel: v1 starts with sterile DB, but active-client manual backfill and paper fallback must be honest source facts.
- Maintainability: business formulas must live in Memberships/application layer, not templates, frontend, reports or ad hoc SQL snippets.
- Testability: domain/application command tests matter more than controller-only or snapshot-only tests.

### Current external facts checked

- Django 5.2 is an LTS release and Django 6.0 is the current feature line; Django's docs also cover first-party transactions, migrations, testing and logging. Sources: [Django 5.2 release notes](https://docs.djangoproject.com/en/6.0/releases/5.2/), [Django 6.0 release notes](https://docs.djangoproject.com/en/6.0/releases/6.0/), [transactions](https://docs.djangoproject.com/en/6.0/topics/db/transactions/), [migrations](https://docs.djangoproject.com/en/6.0/topics/migrations/), [testing](https://docs.djangoproject.com/en/6.0/topics/testing/), [logging](https://docs.djangoproject.com/en/6.0/topics/logging/).
- htmx docs show latest stable usage on the 2.x line, while v4 is still beta. Source: [htmx docs](https://htmx.org/docs/).
- .NET 10 is an LTS release with support through 2028-11-14; ASP.NET Core provides Razor Pages/MVC, Blazor server-side/static rendering options, structured logging, error handling, policy authorization and integration testing support. EF Core supports multiple providers including PostgreSQL through Npgsql and has production migration script/bundle workflows. Sources: [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [Microsoft lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-and-net-core), [Razor Pages](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/?view=aspnetcore-10.0), [Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/?view=aspnetcore-10.0), [EF Core providers](https://learn.microsoft.com/en-us/ef/core/providers/), [EF Core migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying), [ASP.NET Core logging](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-10.0), [ASP.NET Core integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0).
- Rails 8 provides a server-rendered productivity stack with Hotwire/Turbo, Active Record migrations, tests and built-in error reporting interfaces. Sources: [Rails 8 release notes](https://guides.rubyonrails.org/8_0_release_notes.html), [Turbo handbook](https://turbo.hotwired.dev/handbook/introduction), [Active Record migrations](https://guides.rubyonrails.org/active_record_migrations.html), [Rails testing](https://guides.rubyonrails.org/testing.html), [Rails error reporting](https://guides.rubyonrails.org/error_reporting.html).
- Laravel's current docs define a release support policy of 18 months bug fixes and 2 years security fixes, plus first-party migrations, testing and exception reporting/logging. Sources: [Laravel releases](https://laravel.com/docs/13.x/releases), [migrations](https://laravel.com/docs/13.x/migrations), [testing](https://laravel.com/docs/13.x/testing), [errors](https://laravel.com/docs/13.x/errors).
- Next.js Server Actions/Server Functions support server-side mutations, but self-hosted production has cache/durable storage concerns and async Server Component unit testing limitations that push more checks into E2E tests. Sources: [mutating data](https://nextjs.org/docs/app/getting-started/mutating-data), [Server Actions](https://nextjs.org/docs/13/app/building-your-application/data-fetching/server-actions-and-mutations), [self-hosting](https://nextjs.org/docs/app/guides/self-hosting), [testing](https://nextjs.org/docs/app/guides/testing).
- PostgreSQL directly supports constraints, generated columns, partial unique indexes and PITR patterns that match the data architecture requirements. Sources: [constraints](https://www.postgresql.org/docs/current/ddl-constraints.html), [generated columns](https://www.postgresql.org/docs/current/ddl-generated-columns.html), [partial indexes](https://www.postgresql.org/docs/current/indexes-partial.html), [backup/PITR](https://www.postgresql.org/docs/current/continuous-archiving.html).
- Hosting backup details vary materially. Google Cloud SQL supports automated backup retention from 1 day to 10 years and PITR; AWS RDS supports automated backup retention up to 35 days; Render paid Postgres PITR is plan-limited to a few days; DigitalOcean Managed PostgreSQL docs describe daily backups retained for 7 days. Sources: [Cloud SQL backups](https://docs.cloud.google.com/sql/docs/postgres/backup-recovery/backups), [Cloud SQL PITR](https://docs.cloud.google.com/sql/docs/postgres/backup-recovery/pitr), [AWS RDS backup retention](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_WorkingWithAutomatedBackups.BackupRetention.html), [Render Postgres backups](https://render.com/docs/postgresql-backups), [DigitalOcean managed database backups](https://docs.digitalocean.com/products/databases/postgresql/how-to/restore-from-backups/).
- Container hosting options are also time-sensitive: Cloud Run is a managed container platform, while AWS App Runner's public page says it stopped accepting new customers on 2026-04-30. Sources: [Cloud Run docs](https://docs.cloud.google.com/run/docs), [AWS App Runner](https://aws.amazon.com/apprunner/).

## 2. Options matrix

### Full-stack options

| Option | Fit | Pros | Cons | Hidden costs | Reversal cost | Confidence |
|---|---:|---|---|---|---|---|
| ASP.NET Core 10 LTS + Razor Pages/MVC + htmx + EF Core/Npgsql + PostgreSQL | High, selected | Strong fit for modular monolith and command/application services; mature auth/authorization, DI, structured logging, error handling, integration testing and deployment tooling; C# types help model command DTOs/value objects; EF Core plus PostgreSQL can cover migrations and relational rules. | More ceremony than Django/Rails for a small internal app; less built-in owner/admin CRUD surface than Django admin; Blazor Server/Interactive Server can add SignalR/circuit complexity if chosen too broadly. | Need explicit decision between Razor Pages/MVC+htmx and Blazor; need reviewed EF migrations/raw SQL for partial indexes and constraints; need package/provider discipline around Npgsql and PostgreSQL-specific tests. | Medium: domain layer and PostgreSQL schema are portable, Razor/EF code is framework-specific. | High, because maintainer preference has selected .NET. |
| Django 5.2 LTS + htmx 2.x + PostgreSQL | High, rejected for now | Strong fit for server-rendered operational app; built-in auth/session/forms/admin/migrations/testing/logging; mature transaction model; Python is readable for domain tests; htmx supports targeted interactive islands without SPA state. | htmx integration is not as framework-native as Hotwire in Rails; Django "apps" can become technical folders unless module boundaries are enforced; rich UI polish needs design discipline. | Need explicit command/application layer instead of putting logic in views/models; need structured logging/correlation IDs and business audit model; need Playwright for reception UX. | Medium: domain/application tests and PostgreSQL schema transfer well, but templates/forms are framework-specific. | High technically, but not selected. |
| Rails 8 + Hotwire/Turbo + PostgreSQL | High | Excellent match for hybrid server-rendered UI; conventions speed CRUD and operational tools; Active Record migrations/tests are mature; Hotwire is first-class for server-owned state and fast interactions. | Ruby/Rails expertise may be less available; Active Record callbacks can hide business effects if not controlled; Rails conventions can blur module boundaries without discipline. | Need explicit service/command layer to avoid formulas in models/controllers; need careful callback policy so audit/recalculation stays command-owned. | Medium: PostgreSQL/domain concepts portable, Rails views/models less so. | High if Ruby/Rails expertise exists. |
| Laravel 13 + Blade/Livewire + PostgreSQL | Medium-High | Productive server-rendered stack; broad PHP hosting ecosystem; built-in migrations/testing/error handling; Livewire can support interactive forms without a SPA. | Laravel support window is shorter than Django LTS; PHP/Laravel expertise is required; Eloquent patterns can drift into active-record business logic. | Need stricter service/command boundaries; need PostgreSQL-specific indexes/constraints reviewed outside generic migrations. | Medium. | Medium if PHP/Laravel expertise exists. |
| Next.js + Server Actions + PostgreSQL + Drizzle/Prisma | Medium | TypeScript end-to-end; React ecosystem; Server Actions can keep mutations server-side; good if team is strongly TS/React. | More moving parts for a server-rendered internal tool; cache/self-hosting behavior and async Server Component testing add complexity; higher risk that business state leaks into client components. | Need separate ORM/migrations/auth/logging decisions; more E2E testing required; server action deployment/config details must be handled carefully. | Medium-High because UI and data access choices become coupled to React/App Router patterns. | Medium-Low for this domain unless TS/React expertise dominates. |
| Phoenix LiveView + PostgreSQL | Medium | LiveView is a strong server-rendered interactive model; Ecto changesets/migrations fit relational rules; concurrency story is excellent. | Smaller hiring/maintenance pool for many small-business systems; less "boring" if maintainer is not already Elixir/Phoenix fluent. | Need Elixir operational familiarity; support/debugging may depend on specialist skill. | Medium-High. | Medium only with Elixir expertise. |
| FastAPI + Jinja/htmx + SQLAlchemy/Alembic + PostgreSQL | Medium | Explicit command/query style; Python domain tests; SQLAlchemy/Alembic are powerful; lower framework magic. | More assembly work for auth/forms/admin/templates/permissions; less built-in operational app surface than Django. | Need choose/admin/auth/forms/session patterns; more custom glue for every boring feature. | Medium. | Medium as fallback for a team that dislikes Django but wants Python. |

### Layer decisions

| Area | Best-fitting options | Options to avoid for v1 | Rationale |
|---|---|---|---|
| Backend language/framework | Selected: ASP.NET Core 10 LTS. Alternatives considered: Django 5.2 LTS, Rails 8, Laravel 13, Next.js, Phoenix LiveView, FastAPI. | Microservices, API-only backend for hypothetical client portal, event-sourced backend as default. | ADR-002/013 favor modular monolith and no API-first platform. The domain needs boring transactions, auth, forms, reports, migrations and tests. |
| UI approach | Selected: ASP.NET Core Razor Pages/MVC + htmx. Blazor static SSR or small interactive server components may be introduced only after the reception slice proves they do not add device/connectivity fragility. | Full SPA as default; generic admin CRUD as first screen; offline-first PWA; broad Blazor Server dependency for every interaction before proving connection/reconnect behavior on reception devices. | ADR-003 wants reception dashboard, server-rendered pages and interactive islands only where they speed reception. |
| Database | Selected: managed PostgreSQL. | Document DB; spreadsheet-as-database; SQLite for production; MySQL/MariaDB only if PostgreSQL is unavailable and partial/current uniqueness patterns are redesigned. | Data architecture asks for ACID, row locks, FKs/checks, partial unique indexes, reports, audit and backup/restore. PostgreSQL matches most directly. |
| ORM/migrations | Selected: EF Core migrations with Npgsql, plus reviewed SQL migrations where PostgreSQL-specific constraints/indexes require it. | `db push` style production changes, manual DB edits, migration-free schema. | ADR-009/010 require restore rehearsal and safe migration path; ADR-005/006 need source/audit schema from day one. |
| Hosting | Production shortlist: containerized app on managed app platform plus managed PostgreSQL with 30-day backup retention/PITR. Strong candidates: Cloud Run + Cloud SQL, AWS container hosting/ECS + RDS. Simpler PaaS can be used if it adds external 30-day backups. | Local/LAN-first, self-managed DB without tested backups, serverless-only DB with unclear restore, PaaS whose backups cannot meet ADR-009. | ADR-001 allows hosted app; ADR-009 requires backup/restore capability, not just easy deploy. |
| Logging/error handling | Structured technical logs with correlation IDs, framework error reporting, optional Sentry/GlitchTip-like service, provider health checks. Business audit remains in application DB. | Technical logs as business audit; raw PII-heavy logs; no correlation between command/audit/logs. | ADR-006 separates business audit from logs; operations design requires request IDs, error classes, backup status and masking. |
| Testing approach | Domain/application command tests first; DB constraint/migration tests; report reconciliation tests; Playwright E2E for reception dashboard on tablet/phone; restore rehearsal as operational test. | Mostly snapshot tests; UI-only tests; tests against SQLite if production is PostgreSQL; no migration/restore test. | Membership math, audit, reports, backfill and corrections are the risk center. |

### Database options

| Database | Fit | Pros | Cons/Risks | Decision |
|---|---:|---|---|---|
| PostgreSQL managed service | High | ACID, row locks, FKs/checks, partial unique indexes for current card/current rows, generated/stored columns where useful, robust date/report queries, standard backup/PITR ecosystem. | Provider backup retention differs; some hosted Postgres products need add-on backups to meet 30 days. | Selected database. |
| SQLite | Low-Medium | Simple local development, file-based, partial indexes exist. | Production hosted web app with concurrent staff actions, provider backup/PITR, row locking expectations and restore rehearsal are weaker fit. | Use only for local/dev experiments if at all, not production. |
| MySQL/MariaDB managed service | Medium | Mature relational DB, managed providers available. | Current-row uniqueness and filtered indexes may need less direct patterns; PostgreSQL better matches schema requirements and report/query flexibility. | Accept only if provider/team constraint forces it. |
| Document DB | Low | Flexible payloads. | Poor fit for relational constraints, transactions across workflow facts, audit/report drill-down and migrations. | Reject for v1. |

### Hosting options

| Hosting option | Fit | Pros | Cons/Risks | Production condition |
|---|---:|---|---|---|
| Cloud Run + Cloud SQL PostgreSQL | High | Managed container deploy, managed PostgreSQL, configurable backup retention and PITR, low app server maintenance. | GCP setup/IAM/networking has learning curve; cost and region/data policy must be accepted. | Configure >=30-day backups/PITR, staging restore rehearsal, health checks/log retention. |
| AWS container hosting/ECS + RDS PostgreSQL | High-Medium | RDS automated backups up to 35 days, mature recovery tooling. | AWS operational surface is larger; App Runner is not suitable for new customers after 2026-04-30 according to AWS public notice, so use current AWS container options instead. | Keep infrastructure minimal; document restore; avoid overbuilding VPC/networking. |
| Render/DigitalOcean/Railway/Fly style PaaS + managed Postgres | Medium | Very easy app deploy, good developer ergonomics, suitable staging/prototype path. | Built-in backup/PITR windows may be below ADR-009 30-day expectation depending on provider/plan. | Add independent scheduled backups with 30-day retention or choose a plan/provider that proves it; rehearse restore. |
| Single VPS + Docker + managed PostgreSQL | Medium | Cheap, simple mental model, one app process. | OS patching, TLS, process supervision, monitoring and incident response become operator work. | Only if technical operator accepts runbook ownership; still use managed PostgreSQL/backups. |
| Self-managed PostgreSQL on same VPS | Low | Lowest direct cost. | Backup/restore, WAL/PITR, upgrades and monitoring become the project; conflicts with low operational complexity. | Reject for production v1 unless no managed DB is possible. |

## 3. Pros/cons/risks

### Django 5.2 LTS + htmx + PostgreSQL

Pros:

- Fits ADR-003 naturally: templates and forms are server-rendered; htmx can power live search, quick actions and partial profile refreshes.
- Django gives auth/session/admin/forms/migrations/testing/logging without assembling a framework kit.
- `transaction.atomic()` and PostgreSQL row locking can express command boundaries for `MarkVisit`, `IssueMembership`, `CorrectPayment`, `AddFreeze` and `AddNonWorkingDay`.
- Python domain tests are readable for membership calculation cases and edge matrices.
- Django admin can accelerate internal settings/catalog/admin support, as long as state-changing business workflows still go through commands.

Cons:

- Django's default app/model/view shape is not the same as the ADR module map. The implementation must enforce module public commands/queries.
- Too much logic in models, signals or views would violate ADR-004/005. Signals should not become hidden workflow owners.
- htmx partials need disciplined templates to avoid duplicated membership formulas.

Risks:

- Hidden recalculation/audit side effects if developers overuse model `save()` hooks or signals.
- Reports can drift if query code starts recomputing remaining visits instead of reading Memberships state.
- PostgreSQL-specific constraints may need explicit migrations beyond default ORM generation.

### ASP.NET Core 10 LTS + Razor Pages/MVC + htmx + EF Core/Npgsql + PostgreSQL

Pros:

- Strong ADR fit if implemented as a modular monolith with explicit application commands/services and PostgreSQL transaction boundaries.
- .NET 10 LTS has a longer support runway than many web framework feature releases, which helps a small-business system avoid constant platform churn.
- C# records/value objects and dependency injection are good for command DTOs, policy checks, domain services and test seams.
- ASP.NET Core policy-based authorization maps well to Owner/Admin/shared Reception/Admin rules.
- `ILogger`/structured logging and ASP.NET Core error handling fit the technical log side, while business audit remains a first-class table in PostgreSQL.
- EF Core migrations can be reviewed and applied through scripts/bundles; Npgsql gives PostgreSQL access without changing database choice.

Cons:

- It is more verbose than Django/Rails for simple internal screens; developer speed depends heavily on C#/.NET fluency.
- There is no direct Django-admin equivalent for owner/catalog/support screens unless built or scaffolded separately.
- Blazor is attractive, but broad Blazor Server/Interactive Server usage adds SignalR connection/circuit behavior that is not needed for most v1 workflows.
- EF Core abstractions can hide generated SQL; PostgreSQL-specific constraints and report queries need review.

Risks:

- If the team defaults to Web API + SPA because ".NET backend" feels natural, that would fight ADR-003/013.
- If Blazor component state becomes the place where membership status is reasoned about, it can recreate the frontend-state risk ADR-003 rejects.
- If tests use EF Core InMemory for domain persistence behavior, they can miss PostgreSQL constraints, transactions and indexes.
- If hosted on Azure only because of .NET, backup/restore still must be validated against ADR-009 rather than assumed.

### Rails 8 + Hotwire + PostgreSQL

Pros:

- Hotwire/Turbo is one of the strongest matches for hybrid server-rendered UI.
- Rails conventions are fast for internal operational tools and CRUD-adjacent screens.
- Active Record migrations, system tests and built-in error reporting are mature.
- Rails 8 defaults emphasize a cohesive monolith and first-party deployment primitives.

Cons:

- Requires Ruby/Rails familiarity.
- Active Record callbacks can make command boundaries less obvious if used for audit/recalculation.
- Rails "fat model" style can conflict with explicit Memberships ownership unless service/command objects are mandatory for state changes.

Risks:

- Business audit can become callback-driven and hard to reason about.
- Module boundaries can collapse into models/controllers without architecture tests or review rules.

### Laravel 13 + Blade/Livewire + PostgreSQL

Pros:

- Blade and Livewire fit server-rendered interactive workflows.
- Laravel has productive migrations, auth/session foundations, testing and exception reporting.
- PHP hosting is widely available.

Cons:

- Laravel release support cadence creates more regular upgrade pressure than Django LTS.
- Eloquent active-record style needs command/service discipline for recalculation and audit.
- PostgreSQL-specific constraints must be reviewed carefully; many Laravel examples default to MySQL assumptions.

Risks:

- Team may optimize for quick CRUD and underbuild domain tests/audit.
- Provider/hosting choice may pull the project toward cheap shared-style hosting that does not meet backup/restore requirements.

### Next.js + Server Actions + PostgreSQL

Pros:

- TypeScript can provide strong UI/data type feedback.
- Server Actions can keep mutations on the server and align partially with ADR-003.
- React is strong if future UI complexity grows.

Cons:

- The app does not need a full React platform or public API in v1.
- Self-hosting, cache behavior, Server Action configuration, auth, ORM and migrations are separate decisions.
- Testing server-rendered async components often shifts confidence to E2E tests, increasing maintenance for a small internal app.

Risks:

- Client components may accumulate business display logic and duplicated formulas.
- Framework churn/caching semantics may raise operational complexity without domain benefit.
- A TypeScript stack can still be correct, but it needs more guardrails than Django/Rails/Laravel for this ADR package.

### Phoenix LiveView + PostgreSQL

Pros:

- Excellent server-rendered interactive model with server-owned state.
- Ecto is strong for changesets, transactions and migrations.
- Concurrency and realtime patterns are robust if ever needed.

Cons/Risks:

- Maintainer availability is the main risk.
- For a small gym CRM, Elixir/Phoenix may be more specialized than necessary unless the builder already knows it well.

### Cross-cutting risks

- Backup mismatch: a simple PaaS provider may deploy the app easily but fail the 30-day backup expectation. This is the largest operations risk.
- Business audit as logs: any stack can fail if audit is treated as technical logging.
- ORM overreach: any ORM can hide cross-module writes unless command boundaries and code review enforce ownership.
- Report drift: raw SQL report optimization must not duplicate Memberships formulas.
- Backfill shortcuts: direct database edits for opening state or paper fallback would violate ADR-010 regardless of stack.

## 4. Recommended stack or shortlist

### Selected application stack

Selected:

```text
ASP.NET Core 10 LTS + Razor Pages/MVC + htmx + EF Core/Npgsql + PostgreSQL
```

Decision scope:

- Backend language/framework: ASP.NET Core 10 LTS.
- UI: Razor Pages/MVC + htmx for hybrid server-rendered reception/admin screens.
- Database: PostgreSQL.
- Data access/migrations: EF Core with Npgsql; raw/reviewed SQL migrations for PostgreSQL-specific partial indexes, checks and constraints where EF abstractions are not explicit enough.
- Logging/error handling: ASP.NET Core structured logging via `ILogger`, correlation IDs, framework error handling and optional external error reporting; business audit remains a separate append-only PostgreSQL model.
- Testing: xUnit/NUnit-style domain and application command tests, ASP.NET Core integration tests, PostgreSQL-backed persistence tests, Playwright for reception UI, and restore rehearsal as operational testing.

Why this fits BodyLife CRM v1:

- It supports the accepted hosted internal web app and modular monolith ADRs without forcing a public API or SPA.
- Razor Pages/MVC keeps server-rendered pages and forms as the default, while htmx gives the reception dashboard fast search, quick actions, partial refreshes and duplicate-submit UX without client-owned business state.
- ASP.NET Core policy authorization maps cleanly to Owner, named Admin and shared Reception/Admin command policies.
- EF Core/Npgsql plus PostgreSQL can enforce relational consistency, migrations, row locking, constraints, partial unique indexes and report indexes needed by the data architecture.
- C# command DTOs, value objects and application services give a clear home for Memberships recalculation, audit creation and transaction boundaries.
- .NET 10 LTS gives a stable runtime window for a small-business system.

Confidence: high for the application stack, conditional for production readiness until hosting backup/restore is validated.

Recommended hosting shortlist:

1. Cloud Run + Cloud SQL PostgreSQL, if GCP is acceptable and configured with >=30-day automated backup retention plus PITR.
2. AWS container hosting/ECS + RDS PostgreSQL, if AWS is acceptable and the team can keep infrastructure minimal.
3. Simpler PaaS such as Render/DigitalOcean/Railway/Fly only if backup retention is supplemented or proven to meet ADR-009. Use this path freely for prototypes/staging, but not as production-ready until restore rehearsal passes.

### Alternatives retained as fallback knowledge

- Django 5.2 LTS + htmx + PostgreSQL remains the closest non-.NET fallback if the project later loses .NET maintainability.
- Rails 8 + Hotwire/Turbo + PostgreSQL remains a strong fallback if a Rails maintainer takes ownership.
- Laravel 13 + Blade/Livewire + PostgreSQL remains acceptable only if PHP/Laravel expertise dominates.
- Next.js, Phoenix LiveView and FastAPI remain non-selected options for v1 because they add either more assembly, more frontend/runtime complexity, or a narrower maintainer pool for this specific small-business operational app.

### Validation plan before production build lock

Validate the selected stack with a thin vertical slice, not a generic framework benchmark:

1. Implement `IssueMembership`, `MarkVisit`, `CancelVisit`, `CreatePayment`, `CorrectPayment`, `AddFreeze` and `GenerateDailyReport` in ASP.NET Core.
2. Store source facts, `membership_state_cache`, `business_audit_entries`, idempotency keys and session/account metadata in PostgreSQL.
3. Prove transaction behavior: source fact, recalculation and audit commit together or roll back together.
4. Build tablet-size Razor Pages/MVC + htmx reception dashboard: search, client profile, mark visit, payment and daily report drill-down.
5. Run domain tests for inclusive end date, negative visits, cancellation, freeze/non-working overlap, backdated entry and report/profile consistency.
6. Create EF Core migrations plus explicit SQL where needed for partial unique indexes/check constraints and run them against PostgreSQL, not SQLite or EF InMemory.
7. Deploy to staging on the hosting shortlist and rehearse restore into isolated staging before production use.
8. Measure whether the maintainer can debug a failed command using audit entry plus technical logs/correlation ID.

Application stack is selected now. Production readiness can be accepted only after this validation passes and owner/developer agree on hosting backup cost and restore ownership.

## 5. What would change the decision

- Maintainer change: if the long-term maintainer cannot support ASP.NET Core/C#/EF Core, reconsider Django or Rails rather than leaving the business on an unsupported stack.
- Hosting constraint: if only a provider with weak backup retention is acceptable, the stack decision must include external backup automation before production.
- UI validation failure: if Razor Pages/MVC + htmx cannot deliver fast tablet reception UX, evaluate small Blazor islands or a different server-rendered approach before considering SPA.
- EF Core/PostgreSQL mismatch: if EF migrations or generated SQL make critical constraints/report queries opaque, keep EF for ordinary data access but move critical migrations/queries to reviewed SQL or query objects.
- Data residency/compliance: if client data must stay in a specific region/provider, hosting may dominate framework preference.
- Offline-first requirement: if paper fallback is rejected and true offline sync becomes required, the ADR package must change before choosing stack.
- Public client portal/API in v1: if client self-service becomes real scope, reassess API boundary, auth model and frontend approach.
- Multi-tenant/SaaS scope: would require tenant isolation, authorization redesign, backup/restore per tenant policy and likely a new ADR set.
- Heavy long-period financial reporting: may add reporting read models, warehouse/export strategy or accounting integration.
- Full Excel/paper import: would add staging/import tooling and validation workflows, but should still convert through domain commands/audit.
- High concurrent multi-location usage: may require stronger async job patterns, queue choice and scaling strategy.
- Need for many uploaded files: backup scope must include object storage; current v1 assumes database is the primary data asset.

## 6. Migration/backup implications

### Schema and migrations

- Use PostgreSQL as production and test database for integration tests. Do not rely on SQLite for tests that cover constraints, row locks or report queries.
- Create migrations from day one for source facts, derived caches and audit:
  - clients and current card assignments;
  - membership types and issued membership snapshots;
  - visits, visit consumptions and cancellations;
  - payments and corrections/cancellations;
  - freezes and non-working periods/applications;
  - opening states, adjustments, negative closures and entry batches;
  - membership state cache and extension explanation rows;
  - business audit entries;
  - accounts, sessions and roles.
- Add constraints early:
  - foreign keys and not-null/check constraints;
  - partial unique indexes for current card assignment and one active/current row rules;
  - amount/date/range checks;
  - narrow enum-like checks where useful, with migration path for future values.
- Keep EF Core migrations reviewable. Generate SQL for production review, use migration bundles or an explicit deploy migration step, and write explicit SQL migrations for PostgreSQL-specific constraints/indexes where the ORM abstraction is weak.
- Treat destructive data migrations as production incidents unless explicitly planned with backup and restore rehearsal.
- Include seed/bootstrap migration for Owner/named Admin/shared Reception/Admin accounts or a documented first-run setup.

### Data migration/backfill

- V1 starts with sterile database.
- Active clients/memberships can be entered manually through normal commands.
- Incomplete old history uses explicit `membership_opening_states`, not fake visits/payments.
- Backdated and paper fallback entries use `occurred_at`, server `recorded_at`, `entry_origin`, actor/session and reason/comment.
- Future full import should use staging records, validation and command execution, not direct inserts into production source tables.

### Backup and restore

- Production hosting must provide or be supplemented to provide at least 30 days automated backup retention, with RPO not worse than ADR-009's 24-hour expectation and preferably PITR.
- Backup scope includes PostgreSQL database, migration version, app configuration needed for restore and any uploaded files if introduced later.
- Derived tables are rebuildable, but backups must preserve transaction consistency between source facts, derived cache and audit.
- Restore rehearsal is mandatory before production use:
  - restore selected backup into isolated staging;
  - run migrations or verify schema version;
  - compare rebuilt membership state with `membership_state_cache`;
  - verify daily report totals and drill-downs;
  - verify audit for recent visits/payments/freezes/backfill/fallback;
  - owner performs restore-check on known client/report/audit examples.
- Do not use whole-database restore to fix a single wrong visit/payment/freeze. Use correction commands unless there is real data loss/corruption.
- If restore loses post-snapshot business actions, reconcile them as paper/recovery fallback entries, not direct DB patches.

### Deployment implications

- Deploy one web application and one PostgreSQL database.
- Use containerized deployment if it makes staging/production parity and restore rehearsals easier.
- Keep background work minimal in v1. Synchronous recalculation is required for single-membership commands; batch/job processing only for mass non-working day recalculation if v1 scale forces it.
- Run migrations as an explicit deploy step with rollback/restore procedure documented.
- Production readiness requires health check, structured logs, error reporting, backup status visibility and owner-visible restore runbook.

## 7. Implementation starter plan

1. Create final stack validation branch.
   - Create an ASP.NET Core 10 LTS solution.
   - Use Razor Pages/MVC as the first UI surface and htmx for interactive islands.
   - Add EF Core with Npgsql and PostgreSQL from the first migration.
   - Start with PostgreSQL locally and in CI/staging.
   - Add formatting/analyzers, test runner and Playwright from the start.

2. Build modular monolith skeleton.
   - Create top-level modules matching ADR-004.
   - Define command/query interfaces before broad UI.
   - Add shared value objects only for IDs, Money, DateRange and Actor/session context.

3. Implement persistence foundation.
   - Add accounts/sessions/roles.
   - Add clients/card assignments with current-card partial unique constraints.
   - Add membership types and issued membership snapshot schema.
   - Add business audit table and idempotency key storage.

4. Implement Memberships recalculation.
   - Keep formulas in Memberships application/domain service.
   - Cover inclusive end date, remaining visits, negative visits, first negative date, freeze/non-working union days, canceled facts and backdated entries with tests.
   - Store `membership_state_cache` and explanation rows as rebuildable derived state.

5. Build first reception vertical slice.
   - Search client by card/name/phone/last4.
   - Open client profile.
   - Issue membership.
   - Mark visit, including zero/negative warning acknowledgement.
   - Add payment.
   - Show membership state and recent history after canonical reread.

6. Add corrections and reports.
   - Implement cancel visit, correct/cancel payment and add/cancel freeze.
   - Implement daily cash/visits report with drill-down to source rows and audit.
   - Implement ending-soon, low-remaining, negative and inactive report queries through Memberships state.

7. Add owner/settings workflows.
   - MembershipType create/edit/deactivate as Owner-only.
   - NonWorkingDay preview/add/correct/cancel as Owner-only.
   - Shared Reception/Admin session display and honest audit labels.

8. Add operational readiness.
   - Structured logs with request correlation IDs and PII masking.
   - Error reporting integration.
   - Health check endpoint.
   - Backup configuration meeting 30-day retention expectation.
   - Restore runbook and first staging restore rehearsal.
   - Paper fallback template and reconciliation checklist.

9. Record implementation ADR details.
   - Record selected framework/runtime versions.
   - Record database provider and backup retention/PITR settings.
   - Record migration policy, test gates, deploy procedure and restore rehearsal evidence.
   - Keep rejected options and decision-change triggers from this document.
