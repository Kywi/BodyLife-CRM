---
name: bodylife-aspnet-foundation
description: Implementation guidance for BodyLife CRM Milestone 1 foundation. Use when Codex needs to scaffold or reshape the selected ASP.NET Core 10 LTS modular monolith with Razor Pages/MVC, htmx, EF Core/Npgsql, PostgreSQL, CI, analyzers, health checks, local/test PostgreSQL, module folders, command/query conventions, or infrastructure needed before business milestones.
---

# BodyLife ASP.NET Foundation

Use this skill to create or review the production-shaped starting point for BodyLife CRM v1. The stack is already selected: ASP.NET Core 10 LTS + Razor Pages/MVC + htmx + EF Core/Npgsql + PostgreSQL.

## Start Here

Read `references/source-map.md` before editing code. For broad repo context, run a graphify query first when `graphify-out/graph.json` exists.

Implement Milestone 1 as a modular monolith foundation:

1. Create one hosted internal web app, not a public API platform.
2. Keep Razor Pages/MVC as the first UI surface and htmx for small reception-critical islands.
3. Add EF Core with Npgsql and PostgreSQL from the first migration.
4. Create top-level module folders matching the ADR module map: Clients/Search, MembershipTypes, Memberships, Visits, Payments, Freezes, NonWorkingDays, Reports, Audit, Users/Roles.
5. Add shared primitives only for IDs, Money, DateRange, actor/session context, request correlation id, command envelope/results, and error taxonomy.
6. Add test projects/categories for domain, application commands, PostgreSQL integration/migrations, report consistency, and Playwright UI.
7. Add formatting, analyzers, CI gates, health check, and structured logging foundation.

## Foundation Workflow

- Inspect existing repo structure and user changes before scaffolding.
- Prefer the repo's existing conventions when present; otherwise use business-module folders rather than technical-only `Controllers/Services/Models` sprawl.
- Represent module public surfaces as application commands and queries before broad UI.
- Configure local and CI persistence against PostgreSQL for integration behavior.
- Keep migrations reviewable: generated EF migrations plus SQL review or explicit SQL for PostgreSQL-specific constraints.
- Add a minimal bootstrap path for Owner, named Admin, and shared Reception/Admin only if the auth milestone has not already taken ownership.
- Keep the first screen path compatible with reception dashboard work. Avoid a generic CRUD-first app shell.

## Acceptance Checks

- App starts locally and can connect to PostgreSQL.
- CI or local validation runs build, formatting/analyzers, unit tests, PostgreSQL-backed integration tests, and migration apply checks.
- Health check responds in local/staging mode.
- Module boundaries are visible in folders and dependencies.
- Common command envelope/result/error contract exists in application conventions.
- Logs include correlation id, route/command name, duration, outcome, and error class for a smoke request.

## Guardrails

- Do not re-select the stack or introduce SPA/API-first architecture for v1.
- Do not use SQLite or EF InMemory to validate persistence behavior, constraints, row locks, migrations, or report queries.
- Do not put membership formulas in controllers, templates, htmx fragments, or report query shortcuts.
- Do not treat technical logs as business audit.
- Do not add direct DB patch workflows for backfill, fallback, or correction.
- Do not add client portal, public API, offline sync, multi-tenant plumbing, online payments, POS, or full import scaffolding.
