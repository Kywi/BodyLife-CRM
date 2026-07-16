# Source Map

Read the command contract first, then the module-specific docs.

## Primary docs

- `docs/interaction-contracts.md`: section 2 common command contract, section 4 commands, section 6 transaction and consistency rules.
- `docs/implementation-roadmap.md`: cross-cutting rules and the milestone for the command being implemented.
- `docs/operations-design.md`: audit event matrix, backdated/correction audit rules, support/correction workflow.
- `docs/architecture-baseline.md`: implementation implications and quality gates.

## ADRs

- `docs/adr/002-application-architecture.md`: server-side transactions and in-process hooks.
- `docs/adr/003-ui-rendering-and-interaction-model.md`: UI rereads canonical state after commands.
- `docs/adr/005-membership-invariants-and-recalculation.md`: recalculation triggers.
- `docs/adr/006-business-audit-corrections-and-technical-logs.md`: append-only audit.
- `docs/adr/010-migration-manual-backfill-and-paper-fallback.md`: backdated entries via commands.
- `docs/adr/012-permissions-session-accountability-and-corrections.md`: roles and correction boundaries.
- `docs/adr/014-visit-membership-selection-and-freeze-policy.md`: Visit-side Freeze conflict and Membership-first locking.
- `docs/adr/015-freeze-range-eligibility-policy.md`: AddFreeze range eligibility, inverse counted-Visit conflict and transaction lock order.
- `docs/adr/016-non-working-day-application-scope.md`: Preview/Add/CorrectNonWorkingDay eligibility, full applied range, exact confirmed scope and old/new recalculation boundary.

## Module docs by task

- Clients/search/card commands: `docs/adr/008-search-identity-card-rules-and-duplicate-warnings.md`, `docs/data-architecture.md` clients/search schema.
- Membership issue/recalculation commands: `docs/domain-model.md` calculation rules, `docs/adr/005...`, `docs/adr/011...`.
- Visit/payment/freeze/non-working commands: `docs/domain-model.md` lifecycles and correction rules, `docs/data-architecture.md` source facts; AddFreeze also requires ADR-015 and NonWorkingDay commands require ADR-016.
- UI command forms: `docs/ui-workflows.md` relevant workflow and warning/duplicate-submit requirements.
