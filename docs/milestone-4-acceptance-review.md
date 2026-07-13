# Milestone 4 acceptance review

Review date: 2026-07-13.

Source of truth: `docs/implementation-roadmap.md` Milestone 4, ADR-011, `docs/architecture-baseline.md`, `docs/domain-model.md`, `docs/data-architecture.md`, `docs/interaction-contracts.md`, and implementation progress through Step 54.

## Decision

The Milestone 4 MembershipTypes catalog slice is accepted for handoff to Milestone 5.

All behavior owned by MembershipTypes is implemented and tested: Owner-only create/edit/deactivate workflows, retained inactive rows, active-only ordinary issue options, catalog reads, validation, PostgreSQL constraints, audit, idempotency, stale/concurrency handling, canonical rereads, and tablet/phone Owner UI.

One cross-milestone acceptance gate remains explicitly deferred: proving that an issued membership keeps its issue-time snapshot after a later MembershipType edit. The roadmap assigns the issued-membership source fact and snapshot persistence to Milestone 5 and requires the Milestone 4 contract test once that source fact exists. Milestone 4 must be revisited and fully closed in the first Milestone 5 persistence slice; the deferral is not permission to read mutable catalog values from issued memberships.

## Completed foundation

| Roadmap item | Status | Evidence |
|---|---|---|
| MembershipType schema | Done | Migration `20260712192355_AddMembershipTypesCatalog` creates the retained catalog row, lifecycle fields, canonical text/Money checks, positive-duration and non-negative visits/price constraints, and an active issue-query index. |
| Catalog validation | Done | `MembershipTypeCatalogRules` and `MembershipTypeCatalogRulesTests` normalize catalog values and reject missing/invalid names, non-positive duration, negative visit limits, and invalid Money while allowing zero visits and zero price. |
| Public workflows | Done | Typed `CreateMembershipType`, `EditMembershipType`, and `DeactivateMembershipType` commands are the only public catalog mutation contracts; `MembershipTypeContractsTests` verifies there is no public hard-delete command. |
| Command implementation | Done | PostgreSQL-backed handlers enforce canonical Owner authorization, validation, idempotency, row/version concurrency, one transaction, audit, rollback, and canonical reread targets. Edit preserves lifecycle; deactivate updates lifecycle without deleting the row. |
| Catalog/issue query | Done | `GetMembershipTypesForIssue` returns only active rows by default and permits inactive inclusion only for a canonical Owner catalog context, with deterministic ordering and server-provided action metadata. |
| Owner catalog UI | Done | `/Owner/MembershipTypes` renders active and inactive rows and supports create, edit, and active-row deactivation through the command handlers with antiforgery, confirmation, busy/duplicate-submit protection, stable error handling, and Post/Redirect/Get canonical rereads. |
| Business audit | Done | Create, edit, and deactivate append `membership_type.created`, `membership_type.edited`, and `membership_type.deactivated` entries in the command transaction; tests assert actor/session context, summaries, reasons where required, idempotency, and rollback. |
| Issued snapshot integration | Deferred to Milestone 5 | The immutable snapshot fields and `IssueMembership` source-fact transaction do not exist yet by roadmap design. The first Milestone 5 persistence slice must copy and retain type name, duration, visit limit, and price and add the cross-milestone edit-after-issue test. |

## Acceptance criteria

| Acceptance criterion | Status | Notes |
|---|---|---|
| Owner can create, edit, and deactivate MembershipTypes. | Done | Dedicated PostgreSQL command suites and tablet/phone Playwright workflows cover all three actions, validation, stale refresh, canonical reread, and single committed side effects. |
| Admin/shared Reception cannot create/edit/deactivate MembershipTypes. | Done | Every command suite denies named Admin and shared Reception without mutation; Owner-page Playwright proves named Admin cannot navigate to or open the catalog. |
| Inactive types disappear from ordinary issue selection but remain readable in catalog/history/report contexts. | Done at the Milestone 4 boundary | Query tests prove all operational roles receive active-only ordinary options and only Owner catalog context can request inactive rows. Deactivation preserves the row and catalog values. Issued history/report consumption remains downstream work and must use Milestone 5 snapshots rather than the mutable catalog. |
| No application workflow hard-deletes MembershipType. | Done | The public contract exposes create/edit/deactivate only, the UI exposes deactivation only, and storage lifecycle tests prove deactivation preserves the row. |
| Catalog edit creates audit and does not affect already issued snapshot values. | Partially done; cross-milestone gate open | Before/after audit, reason, idempotency, rollback, and no recalculation side effect are proven. The snapshot half is deferred until Milestone 5 creates issued memberships. |
| `GetMembershipTypesForIssue` returns only active types for ordinary issue flow. | Done | Contract guards reject inactive ordinary results, and PostgreSQL query tests prove active-only output for Owner, named Admin, and shared Reception. |

## Test coverage review

| Required test area | Status | Notes |
|---|---|---|
| Create/edit/deactivate commands | Done | PostgreSQL suites cover validation, permission, idempotency, stale/concurrent requests, transaction rollback, audit, lifecycle preservation, and canonical reread targets. |
| Active vs inactive query visibility | Done | PostgreSQL query tests cover ordinary active-only output, Owner inactive inclusion, non-Owner denial, invalid actors, empty catalogs, deterministic ordering, and query-only no-side-effect behavior. |
| PostgreSQL constraints | Done | Storage tests exercise positive duration, non-negative visit limit and price, canonical text/currency, lifecycle consistency, active/inactive retention, and index presence against PostgreSQL. |
| Issued snapshot contract | Deferred to Milestone 5 | Add a PostgreSQL-backed issue-then-edit test as soon as `issued_memberships` exists; it must prove all four copied values remain unchanged and historical reads use the snapshot. |
| Owner catalog UI and Admin denial | Done | Nine Playwright cases cover catalog, create, edit, and deactivate behavior across tablet/phone; the catalog suite also proves named Admin route/navigation denial. |

## Scope and risk check

- The feature remains a compact Owner catalog rather than a broad settings area.
- Duplicate/similar names remain allowed by explicit v1 policy; validation does not invent a uniqueness rule.
- No hard delete, product taxonomy, discounts, subscriptions, family modeling, online sales, promo codes, POS, or accounting integration was added.
- Edit and deactivate change future-sale catalog state only and trigger no Memberships recalculation.
- No UI, report, or query computes issued-membership values from live catalog fields.
- The primary residual risk is accidental mutable-reference use when `IssueMembership` is implemented. The deferred snapshot test is a mandatory Milestone 5 gate.

## Transition to Milestone 5

No MembershipTypes-owned blocker remains. The next step may start only the Milestone 5 domain/application contract for immutable issue-time snapshot values and inclusive base-end-date behavior. It should define and test the rules without adding persistence, recalculation, cache tables, or UI in the same step.

The later Milestone 5 persistence slice must:

- store `membership_type_id` plus immutable type-name, duration, visit-limit, and Money snapshot fields;
- copy those fields from an active canonical MembershipType in the `IssueMembership` transaction;
- calculate inclusive `base_end_date = start_date + duration_days - 1 day` from the snapshot;
- prove a later MembershipType edit leaves every issued snapshot field unchanged;
- keep history and reports on issued snapshots rather than mutable catalog values.

## Validation baseline after Step 54

Focused MembershipTypes evidence:

- Core contract/rule tests: 20 passed.
- PostgreSQL storage/command/query tests: 38 passed against Docker PostgreSQL.
- Playwright catalog/create/edit/deactivate tests: 9 passed across the configured tablet/phone cases, including named Admin denial.

Full repository gate:

```bash
CONFIGURATION=Release DOTNET_ROOT=/tmp/bodylife-dotnet DOTNET_BIN=/tmp/bodylife-dotnet/dotnet BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING='Host=localhost;Port=55432;Database=postgres;Username=bodylife;Password=bodylife_dev_password' ./scripts/validate.sh
```

Result: passed with Release build 0 warnings/errors, formatting/analyzers, 54 core tests, 35 web tests, 145 PostgreSQL infrastructure tests, 24 Playwright smoke tests, and EF migration listing through `20260712192355_AddMembershipTypesCatalog`.

`dotnet-ef migrations has-pending-model-changes` reported no model drift. The running Development app returned `200 OK` from `/health/ready` against the current PostgreSQL schema.
