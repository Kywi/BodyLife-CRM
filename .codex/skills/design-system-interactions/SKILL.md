---
name: design-system-interactions
description: System interaction and application architecture design for BodyLife CRM and similar operations tools. Use when Codex needs to compare interaction styles, module boundaries, APIs, frontend/backend contracts, workflow orchestration, transaction boundaries, error handling, authorization flows, background jobs, sync/offline behavior, or integration architecture without assuming a specific framework or distributed architecture.
---

# Design System Interactions

## Overview

Use this skill to design how the system's parts communicate and where behavior belongs. Keep the first version practical while still comparing interaction patterns and their tradeoffs.

## Workflow

1. Map user workflows.
   - If `graphify-out/graph.json` exists, run `graphify query "<interaction architecture question>" --budget 2500`.
   - Trace the main flows: search client, create client, issue membership, record visit, cancel visit, record payment, freeze membership, add non-working day, view reports, view inactive clients.
   - Note roles and permissions for owner, administrator, and any unresolved trainer access.

2. Identify boundaries.
   - Separate UI concerns, application services/use cases, domain rules, persistence, reporting, audit/history, authentication, and operational tooling.
   - Compare modular monolith, layered architecture, feature modules, service split, event-assisted flows, local-first, and other candidates only where relevant.

3. Define interaction contracts.
   - Compare API styles: server actions, REST, RPC, GraphQL, command/query separation, direct database access from trusted server code, or desktop/local approaches.
   - Define command inputs, validation, authorization, side effects, audit creation, and error responses.
   - Decide which reads need query models or report-specific endpoints.

4. Design transaction and consistency boundaries.
   - Visit marking, visit cancellation, membership issue, cash payment, freeze, non-working day extension, and report correction flows need explicit state-change boundaries.
   - Identify idempotency, concurrency, retry, and duplicate-submission risks.

5. Compare operational consequences.
   - Browse current docs when claims depend on framework capabilities, realtime/sync features, hosting limits, auth providers, or queue behavior.
   - Evaluate how each approach affects debugging, testing, observability, audit, deployment, and recovery.

6. Produce an interaction design.
   - Use `references/interaction-decision-checklist.md`.
   - Include flow diagrams or sequence diagrams when helpful.
   - Present the interaction pattern as a shortlist unless the user asked for a final decision.
   - End with open questions and ADR candidates.

## Guardrails

- Do not introduce distributed services before the domain boundaries and operational need justify them.
- Do not put business rules only in UI components.
- Do not let report queries silently redefine business rules.
- Do not design API endpoints before understanding commands, side effects, and audit requirements.
- Do not frame a pattern as final without naming the assumptions that make it fit.
