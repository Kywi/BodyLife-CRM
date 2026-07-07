---
name: bodylife-reports-audit-history
description: Implementation guidance for BodyLife CRM Reports and Audit milestones. Use when Codex implements or reviews daily cash/visits reports, ending-soon, low-remaining, negative clients, inactive clients, drill-down consistency, corrections/cancellations in reports, GetClientHistory, GetAuditTimeline, append-only business audit, audit UI/history, or report/audit consistency tests.
---

# BodyLife Reports and Audit History

Use this skill when work touches owner trust: reports, client history, audit timelines, corrections, and drill-down explanations.

## Start Here

Read `references/source-map.md` before implementing report queries or audit/history UI.

## Reports Rules

- Reports are query services over canonical source records and Memberships public state.
- Daily reports count active, non-canceled visits and active, non-canceled cash payments for the selected business date.
- Every total must have drill-down rows to source records and relevant correction/cancellation/audit context.
- Corrections after a reconciled day change live totals but must be labeled and explainable.
- Ending-soon, low-remaining, and negative reports read Memberships state; they do not recompute formulas.
- Inactive clients use last counted visit and exclude canceled visits.

## Audit Rules

- Business audit is append-only and separate from technical logs.
- Audit is written for successful commands/workflows, not for ordinary read queries unless a future policy says otherwise.
- Corrections/cancellations add new entries and preserve original facts.
- Required audit fields include actor/account, role, session/device, action type, entity refs, related ids, entry origin, occurred_at, recorded_at, before/after or domain summary, reason/comment where required, correlation id, and idempotency key when applicable.
- `GetAuditTimeline` explains commands; reports do not compute totals from audit.

## Implementation Workflow

1. Identify canonical source tables/read models for the report or history view.
2. Add or review query indexes before relying on UI performance.
3. Return totals and drill-downs from the same source filter.
4. Link rows to client profile, source fact, correction/cancellation fact, and audit where available.
5. Add consistency tests before polishing UI.

## Guardrails

- Do not put Memberships formulas in report SQL/query code.
- Do not use technical logs as business audit.
- Do not UPDATE/DELETE audit rows through application workflows.
- Do not make exported snapshots or report caches source of truth.
- Do not add long-period accounting, tax reports, app-level backup/export UI, or client-facing reports in v1.
