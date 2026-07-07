---
name: bodylife-membership-rules
description: Implementation guidance for the BodyLife CRM Memberships module. Use when Codex implements or reviews issued membership snapshots, MembershipType snapshot copying, inclusive date arithmetic, remaining visits, negative visits, first negative visit date, freeze and non-working day extension union rules, membership_state_cache, recalculation services, membership warnings, opening states, rebuild checks, or Memberships public queries.
---

# BodyLife Membership Rules

Use this skill for the domain core of BodyLife CRM. Memberships is the sole owner of active status, remaining visits, negative balance, first negative visit date, effective end date, extension days, and membership warnings.

## Start Here

Read `references/source-map.md` before editing membership code or tests.

## Required Model

- Store issued membership source facts and immutable issue-time snapshot fields: type name, duration days, visits limit, and price.
- Treat `base_end_date = start_date + duration_days - 1 day` unless the project records a newer product decision.
- Treat `effective_end_date` as derived state. Never edit it directly in ordinary workflows.
- Store rebuildable derived state in `membership_state_cache`.
- Store explainable extension rows in `membership_extension_days`.
- Support `membership_opening_states` for honest manual backfill when old active membership history is incomplete.

## Recalculation Rules

Recalculate from source facts:

- issued membership snapshot
- opening state or explicit audited adjustments
- active counted visit consumptions and non-canceled visits
- active freezes
- applicable active non-working period applications
- negative closure facts when implemented
- corrections/cancellations/backdated entries

Core values:

- counted visits exclude canceled visits.
- remaining visits is signed and can be negative.
- negative balance is `max(0, -remaining_visits)`.
- first negative visit date is the earliest counted visit date that makes running remaining visits less than 0.
- extension days count unique calendar days across active freezes, non-working days, and explicit adjustments.
- active-by-date uses inclusive end date: `today <= effective_end_date`.

## Implementation Workflow

1. Add or update source facts in their owning module command.
2. Call Memberships recalculation synchronously inside the same transaction for single-membership commands.
3. Delete/rebuild derived extension explanation rows for affected memberships.
4. Update `membership_state_cache` as rebuildable derived state.
5. Return public read models through `GetMembershipState` or profile queries.
6. Add domain tests before trusting UI/report behavior.

## Guardrails

- Do not let UI, Reports, Visits, Payments, Freezes, or NonWorkingDays duplicate membership formulas.
- Do not hide negative visits because a payment or new membership exists; require explicit coverage or closure facts.
- Do not directly patch `effective_end_date`, remaining visits, or negative state.
- Do not generate fake history for manual backfill.
- Do not validate persistence behavior with SQLite or EF InMemory.
