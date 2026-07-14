# Milestone 5 acceptance review

Review date: 2026-07-14.

Source of truth: `docs/implementation-roadmap.md` Milestone 5, ADR-004, ADR-005,
ADR-014,
`docs/architecture-baseline.md`, `docs/domain-model.md`,
`docs/data-architecture.md`, `docs/interaction-contracts.md`, and implementation
progress through Step 85.

## Decision

The implemented Milestone 5 Memberships foundation is accepted only for the
completed issue/opening-state/query boundaries. It is the canonical source for
the calculated values it currently exposes, but Milestone 5 is not closed for
handoff to Visit implementation.

ADR-014 closes the product-decision gate: multiple lifecycle-active Memberships
are allowed, membership Visits always identify one explicitly, no-active state
requires an explicit expired or one-off/trial choice, and active Freeze blocks
membership consumption. One technical closure gate remains: Memberships still
needs the required pure source-fact calculation and tests for counted visits,
negative transition, first negative visit, and cancellation. The adjustment
table also remains storage-only and is not yet an input to cache rebuilding.

## Acceptance evidence

| Roadmap area | Status | Evidence |
|---|---|---|
| Source and derived storage | Done | Reviewable EF Core migrations create `issued_memberships`, `membership_opening_states`, `membership_adjustments`, `membership_state_cache`, and `membership_extension_days` with PostgreSQL constraints and indexes. |
| Immutable issue snapshot | Done | `MembershipIssueTerms`, issued-membership storage, and `IssueMembership` copy type name, duration, visit limit, and price. PostgreSQL tests prove later MembershipType edits do not change issued snapshots. |
| Inclusive date rules | Done | Memberships owns `base_end_date = start_date + duration_days - 1 day`, active-by-date behavior, effective end date, and calendar overflow validation. |
| Canonical calculated state | Partial | Memberships owns the state shape, initial/opening calculations, extension-day union, stored-cache invariants, warnings, and read projection. It does not yet derive counted/remaining/negative/first-negative values from synthetic Visit source facts or cancellation state, which the Milestone 5 test plan requires before Visit persistence. |
| Opening state and repair boundaries | Partial | Opening-state commands are audited and rebuildable, and atomic cache/extension persistence is tested. `membership_adjustments` has constrained source-fact storage but no command or recalculation/rebuild input yet. |
| Issue and preview workflows | Done for the accepted no-payment path | Preview and issue require explicit negative handling when existing negative state is present. Only `LeaveVisible` is currently executable; payment/closure paths are explicitly deferred and old negative state is never hidden. |
| Public reads and profile composition | Done | `GetMembershipState` and `GetClientMembershipStates` return canonical Memberships state. `GetClientProfile` composes that public query, exposes no arbitrary current membership on ambiguity, and fails without partial optimistic state when recalculation is unavailable. |
| Formula ownership guardrail | Done | `MembershipFormulaOwnershipTests` scans Core, Infrastructure, and Web IL. Production types outside the Memberships core/infrastructure owners may reference only an explicit public contract allowlist; direct use of formula implementations fails the gate. |

## Acceptance criteria

| Acceptance criterion | Status | Notes |
|---|---|---|
| Issued snapshots remain immutable after catalog edits. | Done | Covered by PostgreSQL issue/storage integration tests. |
| Inclusive base/end-date behavior is canonical. | Done | Covered by focused domain and persistence rehydration tests. |
| Cache state is derived and rebuildable. | Partial | Rebuild is proven for issued and opening-state sources. Adjustment consumption and the future Visit source adapter are not implemented. |
| Public Memberships queries own calculated state and warnings. | Done for implemented sources | Profile composition copies public read values and the architecture gate rejects unapproved formula dependencies. Query ownership does not replace the missing Visit source-fact calculation. |
| Direct effective-end-date edit is unavailable. | Done | Effective end date is produced from base date plus canonical extension-day union; ordinary mutation contracts expose no direct setter/edit command. |
| Issue with negative state requires an explicit decision. | Done for current path | Missing decisions fail; `LeaveVisible` preserves the old negative state; unavailable cover/closure options cannot execute. |
| Profile rereads canonical Memberships state. | Done | The client profile handler invokes the public client-state query and propagates nested failures. |
| Recalculation failure rolls back commands. | Done for implemented commands | Opening-state, issue, rebuild, and atomic persistence integration tests cover rollback and no partial audit/cache/source effects. Visit/cancellation rollback remains Milestone 6 work after the calculation boundary exists. |

## Required-test gap

The roadmap explicitly requires domain tests for remaining visits from counted
visits, multiple negative visits, first negative date, and cancellation
recalculation hooks using synthetic/source fixtures. Current tests can
rehydrate and validate those values from stored cache shapes, but there is no
Memberships-owned calculation from ordered active/canceled Visit facts yet.
Stored-cache validation is not evidence for that missing formula.

With ADR-014 accepted, close this gap with a pure Memberships input contract and
calculator tests before adding PostgreSQL Visit commands.
Also decide whether active `membership_adjustments` participate in rebuild now
or are explicitly deferred together with a future adjustment command; no
workflow should create adjustment rows that rebuilding ignores.

## Product-decision gate

| Decision | State before Milestone 6 |
|---|---|
| Inclusive date convention | Resolved and locked by tests. |
| Multiple active memberships and visit allocation | Resolved by ADR-014. Multiple lifecycle-active rows are allowed; `MarkVisit` always carries explicit `membership_id`, and ambiguous candidates are never auto-selected. |
| Visit without an active membership | Resolved by ADR-014. Actor explicitly selects an expired Membership with acknowledgements or one-off/trial without consumption; there is no default. |
| Visit during an active freeze | Resolved by ADR-014. Membership consumption is blocked for an inclusive active Freeze range; correct/cancel Freeze or use explicit one-off/trial. |
| One-off negative closure | Explicitly deferred to Milestone 7 Payments. Current issue workflow supports only leaving prior negative state visible. |

The product-policy dependency is now satisfied. No `visits`,
`visit_consumptions`, or `visit_cancellations` schema or command work should
begin until the next small slice closes the pure Memberships calculation gap
and explicitly resolves adjustment participation in rebuild.

## Scope check

- No Visit, Payment, Freeze, NonWorkingDay, report, or import workflow was added
  to close this milestone.
- Existing extension fixtures prove date-union behavior, while stored-cache
  fixtures verify consistency only. They do not yet prove Visit source-fact
  calculation or cancellation recalculation.
- Search-result membership summaries and issue-membership Razor/htmx UI remain
  later reception vertical-slice work; they do not replace canonical query
  ownership.
- This review records a sound partial Memberships foundation and its remaining
  closure gates; it does not accept Milestone 5 as complete or imply production
  or go-live readiness.

## Validation baseline after Step 84

The final validation result for this review is recorded in
`docs/implementation-progress.md` Step 84.

## Policy update after Step 85

ADR-014 and the synchronized domain/interaction/roadmap documents resolve the
Visit allocation and Freeze policy questions without adding Visit persistence
or changing the remaining technical acceptance gaps above.
