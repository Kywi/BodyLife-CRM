---
name: bodylife-postgres-data-access
description: Implementation guidance for BodyLife CRM EF Core/Npgsql/PostgreSQL persistence. Use when Codex designs or edits migrations, DbContext mappings, PostgreSQL constraints, partial unique indexes, row locks, transaction tests, source fact tables, derived caches, audit tables, report query indexes, local/test databases, migration bundles, or PostgreSQL-backed integration tests.
---

# BodyLife PostgreSQL Data Access

Use this skill for persistence implementation in the selected stack: EF Core with Npgsql and PostgreSQL. PostgreSQL behavior is part of the product confidence, not an interchangeable detail.

## Start Here

Read `references/source-map.md`, then inspect existing migrations, DbContext conventions, and any dirty user changes.

## Persistence Principles

- Model source facts as canonical tables; model derived state as rebuildable caches owned by the relevant module.
- Use PostgreSQL constraints for invariants that must never be violated.
- Use domain/application validation plus tests for cross-table rules that are too complex for SQL.
- Keep EF Core migrations reviewable. Generate SQL for production review and use explicit SQL where EF hides critical PostgreSQL behavior.
- Use row locks or transaction isolation deliberately for concurrent card assignment, visit marking, negative closure, and correction workflows.
- Add indexes when introducing report/search queries, not as an afterthought.

## Required PostgreSQL Features

- Foreign keys, not-null constraints, check constraints, and narrow enum-like checks where useful.
- Partial unique indexes for current card assignment and one active/current row rules.
- Date/range checks such as `start_date <= end_date`.
- Positive amount and non-negative price/visit constraints.
- Unique cache rows such as one `membership_state_cache` row per membership.
- Composite indexes for daily reports, membership recalculation, search, and audit timelines.

## Testing Rules

- Run migration and persistence behavior tests against PostgreSQL.
- Use Testcontainers, docker compose, or an existing PostgreSQL test database according to repo convention.
- Do not use EF InMemory or SQLite for constraints, transactions, row locks, partial indexes, migrations, or report query behavior.
- Add rebuild comparisons for derived state such as `membership_state_cache`.

## Guardrails

- Do not create direct DB patch workflows for backfill/fallback/corrections.
- Do not make exported snapshots or report caches source of truth.
- Do not store business audit only in logs or JSON blobs without queryable required fields.
- Do not let ORM convenience bypass module ownership or command boundaries.
- Do not treat destructive migrations over source/audit tables as routine.
