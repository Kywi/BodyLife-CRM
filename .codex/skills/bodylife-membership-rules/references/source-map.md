# Source Map

## Primary docs

- `docs/domain-model.md`: entities, invariants, calculation rules, correction/cancellation rules, edge cases, domain test scenarios.
- `docs/data-architecture.md`: source facts, derived state, schema outline for issued memberships, opening states, state cache, extension days.
- `docs/interaction-contracts.md`: `IssueMembership`, `MarkVisit`, `CancelVisit`, `AddFreeze`, `AddNonWorkingDay`, `GetMembershipState`, `PreviewIssueMembership`.
- `docs/implementation-roadmap.md`: Milestone 5 memberships, plus Milestones 6 and 8 for visit/freeze/non-working recalculation.

## ADRs

- `docs/adr/004-module-boundaries-and-business-rule-ownership.md`: Memberships owns formulas.
- `docs/adr/005-membership-invariants-and-recalculation.md`: source facts plus derived state, negative visits, overlap union.
- `docs/adr/010-migration-manual-backfill-and-paper-fallback.md`: opening/backdated facts through commands.
- `docs/adr/011-membership-type-lifecycle.md`: editable catalog plus immutable issued snapshot.
- `docs/adr/014-visit-membership-selection-and-freeze-policy.md`: Visit-side Freeze eligibility and shared lock order.
- `docs/adr/015-freeze-range-eligibility-policy.md`: lifecycle/start eligibility, unclipped end and counted-Visit conflict for Freeze extension facts.
- `docs/adr/016-non-working-day-application-scope.md`: lifecycle/date eligibility, full-period contribution, confirmed scope snapshot and correction behavior for NonWorkingDay applications.

## Business requirements

- `docs/first-version-requirements.md`: sections 7.1-7.11 for formulas and warnings, section 14 for edge cases.
- `docs/vertical-slice-plan.md`: freeze, visits to zero/negative, cancel negative visit, profile/report/audit consistency.
