# Milestone 1 acceptance review

Date: 2026-07-09

Status: foundation ready for Milestone 2 handoff, with production-readiness evidence still reserved for later roadmap milestones.

This review checks Milestone 1 from `docs/implementation-roadmap.md` against the implementation now present in the repository. It is intentionally narrow: no Milestone 2 business/auth implementation is added here.

## Completed foundation

| Area | Status | Evidence |
|---|---|---|
| Hosted internal web app scaffold | Done | `src/BodyLife.Crm.Web`, Razor Pages root route to `/Reception/Index`, no public API-first shell. |
| Modular monolith folders | Done | `src/BodyLife.Crm/Modules/*` contains Clients/Search, MembershipTypes, Memberships, Visits, Payments, Freezes, NonWorkingDays, Reports, Audit and Users/Roles markers. |
| Narrow shared primitives | Done | Shared IDs, Money, DateRange, actor/session context and request correlation primitives exist under `src/BodyLife.Crm/SharedKernel`. |
| Command/query conventions | Done | Command envelope/result/status/error taxonomy and query handler interfaces exist under `src/BodyLife.Crm/Application`. |
| PostgreSQL EF Core setup | Done | `BodyLife.Crm.Infrastructure` wires EF Core/Npgsql, schema `bodylife` and migration history table settings. |
| Baseline migration workflow | Done | Initial baseline migration creates the technical schema/history baseline; `scripts/generate-migration-sql.sh` produces reviewable SQL. |
| Health checks | Done | `/health/live`, `/health/ready` and `/health` are mapped; PostgreSQL readiness uses EF Core connectivity. |
| Structured request logging | Done | JSON console logging, request correlation middleware and request outcome logging are wired. |
| Analyzer/build gate | Done | `Directory.Build.props`, `.editorconfig`, `global.json` and `scripts/validate.sh` define the shared validation gate. |
| CI gate | Done | GitHub Actions runs the shared validation script and provides PostgreSQL service credentials for integration tests. |
| Test projects | Done | Unit, PostgreSQL infrastructure and Playwright UI smoke projects exist under `tests/`. |
| Idempotency key storage | Done | `bodylife.command_idempotency_keys` is added by EF migration with PostgreSQL unique and check constraints for duplicate-submit foundation. |
| Reception-first UI entry | Done | First page is a minimal reception dashboard shell, not a landing page or generic CRUD surface. |
| Playwright smoke harness | Done | Tablet and phone smoke tests start the real app and check the reception entry. |

## Acceptance criteria check

| Roadmap criterion | Current result |
|---|---|
| App starts locally with PostgreSQL rather than SQLite/EF InMemory for integration scenarios. | Met for local development via Docker Compose. `docker-compose.yml` runs PostgreSQL on `localhost:55432`; Development config provides both app and disposable-test admin connection strings. No SQLite/EF InMemory provider is used. |
| CI runs build, formatting/analyzers, unit tests, PostgreSQL-backed integration tests and migration apply check. | Met by configuration. CI provides PostgreSQL service credentials and calls `scripts/validate.sh`. Local run cannot prove GitHub execution, but the gate is shared. |
| Baseline migration creates technical minimum without business shortcut tables. | Met. The baseline creates schema/history table only. |
| Top-level modules have explicit ownership boundaries and no direct cross-module writes. | Met for scaffold. Marker modules and architecture tests exist; no business writes exist yet. |
| Common command envelope/result/error contract is represented. | Met. Command envelope includes actor, correlation id, entry origin, occurred time, idempotency key, reason and comment. Command result carries reread/audit/error shape. |
| Structured logs include correlation id and route/command outcome for a smoke request. | Met for request route logging. Command-level logs wait for real commands. |
| Health check works in local/staging mode. | Met locally with Docker PostgreSQL. Live health works without PostgreSQL; ready health is covered by PostgreSQL-backed infrastructure tests. |
| No generic CRUD-first UI bypassing command/query boundary. | Met. The only UI is the reception entry shell. |

## Remaining roadmap handoff items

1. Owner/Admin/shared Reception bootstrap is not implemented.

   The roadmap allows this to be done in Milestone 2 if not owned by Milestone 1. Because Milestone 2 is Auth/users/roles, bootstrap should be handled there rather than adding unsafe default credentials now.

2. Production backup/restore evidence is not part of Milestone 1.

   Health/logging foundations exist, but backup retention, restore rehearsal and owner restore-check remain Milestones 11 and 12 work.

## Decision

Do not move directly into broad business workflows yet. Milestone 1 foundation work is ready to hand off to Milestone 2, which should start with accountable auth/users/roles and bootstrap ownership before reception business commands.
