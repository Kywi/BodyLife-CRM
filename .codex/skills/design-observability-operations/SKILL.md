---
name: design-observability-operations
description: Observability, audit, logging, deployment, backup, and operations design for BodyLife CRM and similar small-business systems. Use when Codex needs to design or compare business audit history, technical logs, metrics, traces, alerting, error reporting, backup/restore, deployment, monitoring, privacy-safe logging, retention, incident response, or support workflows without assuming a specific hosting or observability vendor.
---

# Design Observability Operations

## Overview

Use this skill to design how BodyLife CRM is operated, debugged, audited, backed up, restored, and supported. Treat business audit history and technical observability as related but separate concerns.

## Workflow

1. Extract operational requirements.
   - If `graphify-out/graph.json` exists, run `graphify query "<observability logging operations question>" --budget 2500`.
   - Read relevant sections for history/transparency, payments, visits, freezes, non-working days, daily report, roles, edge cases, and unresolved questions.
   - Identify who needs the information: owner, administrator, developer, operator, or future support person.

2. Separate the record types.
   - Business audit/history: user-visible or owner-reviewable facts about important actions.
   - Technical logs: debugging/runtime records.
   - Metrics: counters, durations, rates, and health indicators.
   - Traces: request/workflow path when complexity justifies it.
   - Backups and restore logs: evidence that recovery works.

3. Compare approaches.
   - Compare file logs, database audit tables, structured JSON logs, hosted log platforms, open-source observability stacks, lightweight uptime monitoring, and framework-native tools where relevant.
   - Browse current docs/pricing/features when recommending a service, library, hosting provider, or retention model.
   - Evaluate privacy, cost, setup complexity, maintenance, and recovery value.

4. Design minimum viable operations.
   - Use `references/operations-checklist.md`.
   - Define what must be logged, what must never be logged, what must be audited, what metrics matter, how backups work, how restore is tested, and how incidents are handled.
   - Include local/dev, staging if applicable, and production behavior.

5. Validate with failure scenarios.
   - Wrong visit marked.
   - Cash payment corrected.
   - Freeze entered incorrectly.
   - Non-working day affected many memberships.
   - Database backup must be restored.
   - Server or local machine is unavailable.
   - User reports that a number or report looks wrong.

6. Produce an operations design.
   - Include audit schema/fields, logging policy, metrics list, alerting/monitoring plan, backup/restore plan, retention policy, and open questions.
   - If recommendations depend on stack choice, state the stack-agnostic policy first and technology-specific implementation options second.
   - Prefer policy and option comparison before vendor/tool recommendations.

## Guardrails

- Do not use application logs as the only source of business truth.
- Do not log sensitive personal data unless there is a clear need and masking/retention are defined.
- Do not call backups complete until restore has a tested path.
- Do not add heavy observability tooling when a simpler approach satisfies the risk profile.
- Do not ignore manual support workflows; small systems often fail at recovery, not at dashboard beauty.
- Do not choose an observability vendor or hosting-specific tool before the deployment model is known.
