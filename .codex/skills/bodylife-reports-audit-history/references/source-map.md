# Source Map

## Primary docs

- `docs/interaction-contracts.md`: `GenerateDailyReport`, report list queries, `GetClientHistory`, `GetAuditTimeline`.
- `docs/data-architecture.md`: audit data model, reporting data access, indexes.
- `docs/operations-design.md`: business audit policy, audit event matrix, support/correction workflow.
- `docs/implementation-roadmap.md`: Milestone 9 Reports and Milestone 10 Business audit/history UI.

## ADRs

- `docs/adr/006-business-audit-corrections-and-technical-logs.md`: append-only business audit separate from logs.
- `docs/adr/007-reporting-model-and-consistency-rules.md`: reports over canonical records and Memberships queries.
- `docs/adr/010-migration-manual-backfill-and-paper-fallback.md`: backfill/fallback labels in history.
- `docs/adr/012-permissions-session-accountability-and-corrections.md`: owner/admin audit identity and closed-day corrections.

## Supporting docs

- `docs/domain-model.md`: Reports, Audit, calculation rules, correction/cancellation rules.
- `docs/ui-workflows.md`: daily report flow, correction flows, owner/admin differences.
- `docs/first-version-requirements.md`: sections 9 and 12 for history and report requirements.
- `docs/vertical-slice-plan.md`: report and audit consistency acceptance criteria.
