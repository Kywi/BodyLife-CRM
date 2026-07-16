# Source Map

## Primary docs

- `docs/implementation-roadmap.md`: every milestone has tests, acceptance criteria, risks, and out-of-scope items.
- `docs/vertical-slice-plan.md`: first slice test plan, acceptance criteria, operational checks.
- `docs/architecture-baseline.md`: quality gates before coding.
- `docs/technology-stack-decision.md`: selected stack validation plan and testing approach.

## Test detail docs

- `docs/domain-model.md`: domain test scenarios and edge case matrix.
- `docs/data-architecture.md`: constraints/indexes, restore-check queries, validation scenarios.
- `docs/interaction-contracts.md`: command errors, transaction consistency rules, query expectations.
- `docs/ui-workflows.md`: acceptance checklist for the reception slice.
- `docs/operations-design.md`: production readiness checklist, restore rehearsal and paper fallback reconciliation.

## ADRs

- `docs/adr/005-membership-invariants-and-recalculation.md`: membership test requirements.
- `docs/adr/006-business-audit-corrections-and-technical-logs.md`: audit/logging gates.
- `docs/adr/007-reporting-model-and-consistency-rules.md`: report consistency gates.
- `docs/adr/009-backup-restore-and-operational-recovery.md`: restore rehearsal gate.
- `docs/adr/010-migration-manual-backfill-and-paper-fallback.md`: backfill/fallback gates.
- `docs/adr/012-permissions-session-accountability-and-corrections.md`: authorization/accountability gates.
- `docs/adr/014-visit-membership-selection-and-freeze-policy.md`: Visit/Freeze concurrency and blocking tests.
- `docs/adr/015-freeze-range-eligibility-policy.md`: Freeze lifecycle/start bounds, unclipped end, counted-Visit conflict and lock-order tests.
