# Source Map

## Primary docs

- `docs/data-architecture.md`: full persistence source map. Read sections 1-5 before schema work, sections 6-9 for audit/report/backfill/backup.
- `docs/technology-stack-decision.md`: schema/migrations, data migration/backfill, backup/restore, deployment implications.
- `docs/implementation-roadmap.md`: Milestone 1 persistence foundation and relevant module milestone.
- `docs/architecture-baseline.md`: forbidden shortcuts, allowed dependencies, quality gates.

## ADRs

- `docs/adr/005-membership-invariants-and-recalculation.md`: source facts plus derived state.
- `docs/adr/006-business-audit-corrections-and-technical-logs.md`: audit table semantics.
- `docs/adr/007-reporting-model-and-consistency-rules.md`: reports over canonical records.
- `docs/adr/008-search-identity-card-rules-and-duplicate-warnings.md`: card/phone/name indexes and uniqueness.
- `docs/adr/009-backup-restore-and-operational-recovery.md`: backup/restore implications.
- `docs/adr/010-migration-manual-backfill-and-paper-fallback.md`: no direct DB patches.
- `docs/adr/011-membership-type-lifecycle.md`: snapshot persistence and no hard delete.
- `docs/adr/014-visit-membership-selection-and-freeze-policy.md`: Visit/Freeze relational guards and Membership-first locking.
- `docs/adr/015-freeze-range-eligibility-policy.md`: AddFreeze range validation, Visit conflict query and lock order.
- `docs/adr/016-non-working-day-application-scope.md`: exact application snapshot, full-period applied ranges, preview revalidation and old/new correction scope.

## Query-specific docs

- Search/card: `docs/interaction-contracts.md` CreateClient, UpdateClient, AssignOrChangeCard, SearchClients.
- Reports: `docs/interaction-contracts.md` report queries and `docs/data-architecture.md` reporting data access.
- Audit: `docs/operations-design.md` required audit fields and event matrix.
