# Source Map

Read only the documents needed for the task.

## Primary docs

- `docs/technology-stack-decision.md`: selected stack, migration/testing/deployment implications, implementation starter plan.
- `docs/implementation-roadmap.md`: Milestone 1 project scaffold and infrastructure, cross-cutting rules.
- `docs/architecture-baseline.md`: non-negotiable architecture rules, module map, forbidden shortcuts, quality gates.
- `docs/vertical-slice-plan.md`: slice scope, technical flow, tests that prove the foundation is usable.

## ADRs

- `docs/adr/README.md`: accepted ADR package and recommended realization order.
- `docs/adr/001-product-shape-and-operating-model.md`: internal hosted app, paper fallback, no offline/client/SaaS scope.
- `docs/adr/002-application-architecture.md`: modular monolith, one deploy, server-side transactions, local hooks only.
- `docs/adr/003-ui-rendering-and-interaction-model.md`: server-rendered UI with htmx islands.
- `docs/adr/004-module-boundaries-and-business-rule-ownership.md`: top-level modules and Memberships ownership.
- `docs/adr/009-backup-restore-and-operational-recovery.md`: production readiness expectations.
- `docs/adr/013-future-client-self-service-boundary.md`: no API-first/client portal in v1.

## Supporting docs

- `docs/interaction-contracts.md`: section 2 common command contract, section 3 module boundaries, section 6 transaction rules.
- `docs/data-architecture.md`: sections 4, 5, 9 for schema outline, constraints/indexes, migration/backup implications.
- `docs/operations-design.md`: technical logs, health/metrics, backup/restore policy.
- `docs/first-version-requirements.md`: v1 scope, users, main reception workflows, ready criteria.
