---
name: bodylife-command-workflows
description: Implementation guidance for BodyLife CRM state-changing workflows. Use when Codex implements or reviews commands/actions such as CreateClient, IssueMembership, MarkVisit, CancelVisit, CreatePayment, CorrectPayment, AddFreeze, NonWorkingDay changes, backfill, corrections, authorization, idempotency, transaction boundaries, recalculation hooks, business audit entries, canonical rereads, or command error taxonomy.
---

# BodyLife Command Workflows

Use this skill for every BodyLife CRM mutation path. A workflow is not complete until it validates input, authorizes the actor, commits source facts, triggers recalculation when needed, appends business audit, and returns a canonical reread target.

## Start Here

Read `references/source-map.md` for command-specific document routing. For codebase questions, query graphify before broad browsing when the graph exists.

## Command Shape

Every command should carry the operational envelope:

- actor account id, role, account type, session id, optional device label
- request correlation id
- idempotency key for quick actions or correction commands with duplicate-submit risk
- entry origin: `normal`, `manual_backfill`, `paper_fallback`, or future import
- `occurred_at` or business date/range for business facts
- server-set `recorded_at` at successful commit
- reason/comment when required for correction, cancellation, card reassignment, backfill/fallback, or owner-sensitive actions

Return status, primary id, related ids, audit id where created, warnings, changed-after-close marker if relevant, and enough target data for the UI to reread canonical state.

## Implementation Workflow

1. Identify the owning module and public command/query boundary.
2. Enforce server-side authorization before mutation: Owner-only, Admin+Owner, current/open-day correction, or owner-approved after-close policy.
3. Validate business rules and concurrency/stale state.
4. Check idempotency before creating source facts; repeated submits must not duplicate visits, payments, freezes, or corrections.
5. Open one PostgreSQL transaction for source facts, status changes, Memberships recalculation, derived cache updates, and business audit.
6. Use public module interfaces for cross-module behavior. Avoid direct cross-module table writes outside owned workflow coordination.
7. Append owner-readable business audit in the same consistency boundary.
8. Return a canonical reread target such as `GetClientProfile`, `GetMembershipState`, `GenerateDailyReport`, or `GetAuditTimeline`.

## Error Taxonomy

Prefer stable errors from the interaction contracts: `permission_denied`, `validation_failed`, `not_found`, `duplicate_submission`, `stale_state`, `card_number_already_current`, `duplicate_warning_not_acknowledged`, `day_closed_requires_owner`, `membership_not_eligible`, `membership_type_inactive`, `already_canceled`, `reason_required`, `recalculation_failed`, `concurrency_conflict`.

## Guardrails

- Do not mutate business state from UI-only handlers, templates, frontend state, ad hoc scripts, or direct DB patches.
- Do not split source fact, recalculation, and audit into separate commits for single-membership commands.
- Do not hide negative visits, corrections, or backdated entries as side effects.
- Do not use technical logs as the business history.
- Do not use SQLite or EF InMemory to prove transaction, locking, constraint, or idempotency behavior.
- Do not compute Memberships formulas outside the Memberships public API.
