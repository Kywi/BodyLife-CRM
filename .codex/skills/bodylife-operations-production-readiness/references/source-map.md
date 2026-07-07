# Source Map

## Primary docs

- `docs/operations-design.md`: operational goals, technical logs, metrics, backup/restore, paper fallback, support workflow, production readiness checklist.
- `docs/implementation-roadmap.md`: Milestone 11 backup/restore/paper fallback readiness and Milestone 12 production hardening.
- `docs/technology-stack-decision.md`: hosting shortlist, backup/restore implications, deployment implications.
- `docs/data-architecture.md`: migration and backup implications, restore-check queries.

## ADRs

- `docs/adr/006-business-audit-corrections-and-technical-logs.md`: separate technical logs from business audit.
- `docs/adr/009-backup-restore-and-operational-recovery.md`: managed backups, 30-day retention expectation, restore rehearsal.
- `docs/adr/010-migration-manual-backfill-and-paper-fallback.md`: fallback/backdated command requirements.
- `docs/adr/012-permissions-session-accountability-and-corrections.md`: session/accountability and sensitive correction boundaries.

## Supporting docs

- `docs/architecture-baseline.md`: operations gate before coding and production use.
- `docs/vertical-slice-plan.md`: operational checks for command logs and audit correlation.
- `docs/first-version-requirements.md`: business continuity context and v1 scope exclusions.
