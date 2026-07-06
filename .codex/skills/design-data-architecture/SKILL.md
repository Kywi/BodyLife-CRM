---
name: design-data-architecture
description: Data architecture design for BodyLife CRM and similar membership/accounting systems. Use when Codex needs to derive domain entities, compare database models, design schemas, constraints, migrations, audit/history storage, search, reporting data, backup/restore, Excel migration, data retention, or consistency rules without assuming a database technology first.
---

# Design Data Architecture

## Overview

Use this skill to design the data architecture from business rules and invariants. Treat the requirements as domain behavior first, then compare storage models and schema choices.

## Workflow

1. Extract the domain model.
   - If `graphify-out/graph.json` exists, run `graphify query "<data architecture question>" --budget 2500`.
   - Read relevant sections for clients, card numbers, membership types, issued memberships, visits, payments, freezes, non-working days, reports, warnings, history, and edge cases.
   - Capture unresolved questions from the requirements before modeling.

2. Identify invariants and lifecycle rules.
   - Unique card number when present.
   - Client may exist without a card.
   - Membership balance, negative lessons, start/end dates, freezes, non-working-day extensions, cancellations, and cash payments must stay explainable.
   - Reports must remain correct after corrections or cancellations.
   - Business history must show who did what and when.

3. Compare storage approaches.
   - Compare relational, embedded relational, managed relational, document, event-log-assisted, local-first, and hybrid approaches when relevant.
   - Evaluate constraints, transactions, migrations, backups, report queries, search, audit, import/export, and operational simplicity.
   - Browse current database docs when features, limits, pricing, support windows, or hosting claims matter.

4. Design the schema or data model.
   - Use `references/data-decision-checklist.md`.
   - Model source-of-truth fields separately from derived/reporting values.
   - Decide which values are stored, computed, snapshotted, or materialized.
   - Define indexes/search strategy for card number, name, phone, and last four phone digits.

5. Validate with scenarios.
   - Walk through issuing a membership, marking a visit, going negative, paying later, freezing, adding a non-working day, canceling a visit, canceling a freeze, correcting a payment, and generating a daily report.
   - Show how data changes and how audit/history remains understandable.

6. Produce artifacts.
   - Entity list and relationship diagram if useful.
   - Constraint list.
   - Migration/import plan.
   - Backup/restore plan.
   - Shortlist or recommendation with explicit assumptions. Prefer a shortlist when the user has not asked for a final choice.
   - Open questions and ADR candidates.

## Guardrails

- Do not confuse business entities with tables too early.
- Do not store derived values without defining how they are recalculated or audited.
- Do not let corrections delete history that the owner may need for disputes.
- Do not ignore data migration just because it is outside the first UI implementation.
- Do not present a database choice as final unless deployment model, maintenance owner, and backup expectations are known or the user explicitly asks for a decision.
