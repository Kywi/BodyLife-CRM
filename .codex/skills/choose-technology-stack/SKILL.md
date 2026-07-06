---
name: choose-technology-stack
description: Evidence-based technology stack selection for BodyLife CRM and comparable small-business operations systems. Use when Codex needs to compare or choose programming languages, backend frameworks, frontend frameworks, database engines, ORMs, hosting/deployment models, job queues, search tools, reporting tools, or infrastructure choices without assuming a preselected stack.
---

# Choose Technology Stack

## Overview

Use this skill to compare technology stack options from project needs outward. Do not begin with a favorite framework; begin with BodyLife CRM's workflows, data integrity requirements, operational constraints, and future-change pressure.

## Workflow

1. Gather local context.
   - If `graphify-out/graph.json` exists, run `graphify query "<stack decision question>" --budget 2500`.
   - Read the relevant requirement sections for users, workflows, data to store, business rules, reports, history, search, edge cases, and unresolved questions.
   - Identify what the first version explicitly excludes.

2. Define decision criteria before listing tools.
   - Include reception speed, correctness of membership calculations, simple cash reporting, auditability, migration from paper/Excel, backup/restore, low operational burden, testability, maintainability, developer ergonomics, deployment cost, and future integration options.
   - Weight criteria if the user asks for a recommendation.

3. Build a longlist, then a shortlist.
   - Consider language/runtime families, not only named frameworks.
   - Compare plausible backend choices, frontend choices, database/storage choices, deployment models, and supporting tools.
   - Include a minimal viable boring stack option and a more scalable/flexible option when appropriate, but do not assume either wins.

4. Research current facts.
   - Browse official docs and current sources for versions, lifecycle status, hosting limitations, pricing, security posture, ecosystem health, and database features.
   - Cite sources for claims that may change over time.
   - Prefer primary sources over blog summaries.

5. Compare with a scoring matrix.
   - Read `references/selection-matrix.md` when producing the comparison.
   - Score only after explaining tradeoffs; avoid fake precision.
   - Name the assumptions that would flip the decision.

6. End with an implementation-neutral decision record.
   - If the user asked to choose, give a recommendation, confidence level, and fallback.
   - If evidence is insufficient, give a shortlist and validation plan.

## Compare These Areas

- Language/runtime and backend framework.
- Frontend delivery: server-rendered, SPA, hybrid, desktop wrapper, local-first, or other fit.
- Database/storage: relational, embedded relational, managed relational, document, spreadsheet import/export support, backups.
- Data access: ORM, query builder, raw SQL, migrations, test data.
- Auth and authorization support.
- Reporting and search support.
- Hosting/deployment: local machine, VPS, managed app platform, container, serverless, LAN-only, cloud-hosted.
- Operational tooling: logs, audit, metrics, backup, restore, migrations, monitoring.

## Guardrails

- Do not lock the project into a database before modeling the data and transaction rules.
- Do not recommend microservices, event sourcing, offline sync, or cloud services unless their complexity is justified.
- Do not ignore boring operational questions: who runs it, who restores backups, how mistakes are corrected, how audit is preserved.
- Do not treat simple first version as permission to skip migrations, tests, backups, or audit history.
