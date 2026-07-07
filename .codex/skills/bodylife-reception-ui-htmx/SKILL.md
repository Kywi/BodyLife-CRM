---
name: bodylife-reception-ui-htmx
description: Implementation guidance for BodyLife CRM Razor Pages/MVC plus htmx reception UI. Use when Codex builds or reviews reception dashboard, live search, exact card match, compact results, client profile, membership status panel, issue membership, mark/cancel visit, payment, freeze, warning acknowledgement, duplicate-submit protection, daily report links, tablet-first and phone-friendly workflows, or server-rendered htmx partials.
---

# BodyLife Reception UI htmx

Use this skill to build the v1 operational UI. The first screen is the reception dashboard, not a landing page and not generic CRUD.

## Start Here

Read `references/source-map.md`, especially `docs/ui-workflows.md`, before changing screens or htmx partials.

## UI Principles

- Server renders pages, forms, profiles, reports, and settings.
- htmx is allowed for reception-critical islands: live search, compact results, membership state refresh, warnings, quick actions, loading state, and duplicate-submit protection.
- State-changing actions post to server-side commands. After success, reread canonical state from server queries.
- UI displays server-provided membership state and warnings. It does not calculate remaining visits, active status, negative balance, first negative date, extension days, or effective end date.
- Tablet is the primary reception target; phone layout must keep every critical warning and action reachable.

## Required Workflows

- Reception dashboard with current account/session/device indicator.
- Search by exact card, name, phone, and last four phone digits.
- Exact unique current card auto-open; partial or non-unique results render a list.
- Client profile with identity, current card, membership panel, warnings, recent history, and allowed actions.
- Issue membership with active type selector, snapshot preview, payment option, and negative handling warning.
- Mark visit with zero/negative/expired warning acknowledgement.
- Add payment and add/cancel freeze forms.
- Correction forms with confirmation and reason/comment.
- Daily report screen with drill-down links to profile/history/audit.

## Interaction Checks

- Use idempotency keys for IssueMembership, MarkVisit, CreatePayment, AddFreeze, and correction/cancellation forms.
- Disable/busy state must engage on submit/tap and survive fast repeated taps.
- Stale or concurrency errors must ask for canonical refresh.
- Permission hiding in UI is convenience only; server policies remain authoritative.
- Shared Reception/Admin account/session must be visible and honest.

## Guardrails

- Do not build a SPA or API-first frontend for v1.
- Do not add business formulas to Razor templates, htmx fragments, JavaScript, or frontend state.
- Do not leave optimistic membership values as truth after a command.
- Do not hide negative/zero/expired/backfill/fallback/changed-after-close warnings in compact layouts.
- Do not add client portal, online payments, QR/NFC/turnstile, scanner-specific identity, or offline sync.
