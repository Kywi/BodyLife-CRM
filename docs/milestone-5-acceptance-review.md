# Milestone 5 acceptance review

Review date: 2026-07-14.

Source of truth: `docs/implementation-roadmap.md` Milestone 5, ADR-004, ADR-005,
ADR-014,
`docs/architecture-baseline.md`, `docs/domain-model.md`,
`docs/data-architecture.md`, `docs/interaction-contracts.md`, and implementation
progress through Step 87.

## Decision

The implemented Milestone 5 Memberships foundation is accepted and closed for
handoff to Milestone 6 Visit persistence and commands. Memberships is the
canonical owner of every calculated value it exposes, and all source facts
available at this milestone now have a controlled calculation or rebuild
boundary.

ADR-014 closes the product-decision gate: multiple lifecycle-active Memberships
are allowed, membership Visits always identify one explicitly, no-active state
requires an explicit expired or one-off/trial choice, and active Freeze blocks
membership consumption. Step 86 closes the required pure source-fact gate for
counted visits, negative transition, first negative Visit, cancellation and
Visit-date eligibility. Step 87 closes the final technical gate by defining the
two supported active adjustment shapes and including them in cache rebuild.

## Acceptance evidence

| Roadmap area | Status | Evidence |
|---|---|---|
| Source and derived storage | Done | Reviewable EF Core migrations create `issued_memberships`, `membership_opening_states`, `membership_adjustments`, `membership_state_cache`, and `membership_extension_days` with PostgreSQL constraints and indexes. |
| Immutable issue snapshot | Done | `MembershipIssueTerms`, issued-membership storage, and `IssueMembership` copy type name, duration, visit limit, and price. PostgreSQL tests prove later MembershipType edits do not change issued snapshots. |
| Inclusive date rules | Done | Memberships owns `base_end_date = start_date + duration_days - 1 day`, active-by-date behavior, effective end date, and calendar overflow validation. |
| Canonical calculated state | Done for Milestone 5 inputs | Memberships owns the state shape, initial/opening calculations, extension calculation, stored-cache invariants, warnings, read projection, explicit Visit eligibility, deterministic active/canceled Visit calculation and controlled adjustment calculation. |
| Opening state and repair boundaries | Done | Opening-state commands are audited and rebuildable, atomic cache/extension persistence is tested, and adjustment rebuild uses the opening declaration's `recorded_at` as an explicit cutover. |
| Issue and preview workflows | Done for the accepted no-payment path | Preview and issue require explicit negative handling when existing negative state is present. Only `LeaveVisible` is currently executable; payment/closure paths are explicitly deferred and old negative state is never hidden. |
| Public reads and profile composition | Done | `GetMembershipState` and `GetClientMembershipStates` return canonical Memberships state. `GetClientProfile` composes that public query, exposes no arbitrary current membership on ambiguity, and fails without partial optimistic state when recalculation is unavailable. |
| Formula ownership guardrail | Done | `MembershipFormulaOwnershipTests` scans Core, Infrastructure, and Web IL. Production types outside the Memberships core/infrastructure owners may reference only an explicit public contract allowlist; direct use of formula implementations fails the gate. |

## Acceptance criteria

| Acceptance criterion | Status | Notes |
|---|---|---|
| Issued snapshots remain immutable after catalog edits. | Done | Covered by PostgreSQL issue/storage integration tests. |
| Inclusive base/end-date behavior is canonical. | Done | Covered by focused domain and persistence rehydration tests. |
| Cache state is derived and rebuildable. | Done for available source storage | Rebuild is proven for issued snapshots, opening declarations and active supported adjustments. Visit/Freeze/NonWorkingDay adapters remain work of their owning later milestones after those source tables exist. |
| Public Memberships queries own calculated state and warnings. | Done for implemented sources | Profile composition copies public read values and the architecture gate rejects unapproved formula dependencies. Step 86 supplies the future Visit adapter's Memberships-owned calculation boundary. |
| Direct effective-end-date edit is unavailable. | Done | Effective end date is produced from base date plus the canonical extension total; ordinary mutation contracts expose no direct setter/edit command. |
| Issue with negative state requires an explicit decision. | Done for current path | Missing decisions fail; `LeaveVisible` preserves the old negative state; unavailable cover/closure options cannot execute. |
| Profile rereads canonical Memberships state. | Done | The client profile handler invokes the public client-state query and propagates nested failures. |
| Recalculation failure rolls back commands. | Done for implemented commands | Opening-state, issue, rebuild, and atomic persistence integration tests cover rollback and no partial audit/cache/source effects. Visit/cancellation rollback remains Milestone 6 work after the calculation boundary exists. |

## Required-test closure

Step 86 closes the roadmap's pure domain-test gap with Memberships-owned
contracts that require one explicit selected Membership and same-membership,
unique Visit source facts. The calculator orders active counted facts by
`occurred_at`, server `recorded_at`, then stable Visit id; canceled facts remain
in the input but do not affect count, remaining balance, first-negative metadata
or last counted Visit.

Native history starts from the issue-time visit-limit snapshot. The opening
state overload starts from its signed declared balance and deliberately accepts
only Visit facts not already represented by that opening baseline. This avoids
synthetic history and preserves unknown first-negative metadata when the opening
balance was already negative. Seventeen focused tests cover ordering, signed
remaining/negative state, zero-to-negative transition, first-negative movement
after cancellation, positive/negative opening baselines, inclusive Freeze
blocking, future-start/lifecycle rejection and typed expired/zero/negative
acknowledgements.

Step 87 adds eleven focused adjustment-domain cases and four PostgreSQL rebuild
cases. Active `visit_balance` rows adjust signed remaining/negative state without
inventing Visits; active positive `extension_days` rows adjust total extension
and effective end. Inactive history is retained and ignored, while active
money, unknown, mixed and negative-day shapes fail rebuild without mutating the
cache. An opening declaration includes facts recorded through its own
`recorded_at`, so only later adjustment records are applied.

## Product-decision gate

| Decision | State before Milestone 6 |
|---|---|
| Inclusive date convention | Resolved and locked by tests. |
| Multiple active memberships and visit allocation | Resolved by ADR-014. Multiple lifecycle-active rows are allowed; `MarkVisit` always carries explicit `membership_id`, and ambiguous candidates are never auto-selected. |
| Visit without an active membership | Resolved by ADR-014. Actor explicitly selects an expired Membership with acknowledgements or one-off/trial without consumption; there is no default. |
| Visit during an active freeze | Resolved by ADR-014. Membership consumption is blocked for an inclusive active Freeze range; correct/cancel Freeze or use explicit one-off/trial. |
| One-off negative closure | Explicitly deferred to Milestone 7 Payments. Current issue workflow supports only leaving prior negative state visible. |

The product-policy dependency, pure Memberships Visit-calculation dependency and
adjustment rebuild dependency are satisfied. The next small slice may begin
Milestone 6 with Visit source-fact storage only.

## Scope check

- No Visit, Payment, Freeze, NonWorkingDay, report, or import workflow was added
  to close this milestone.
- Existing extension fixtures prove date-union behavior. Step 86 synthetic
  source fixtures now prove Visit source-fact calculation, deterministic
  first-negative ordering and cancellation recalculation without adding Visit
  persistence.
- Step 87 proves additive adjustment behavior and rollback without fabricating
  Visit or extension-date source rows; the retained adjustment row remains the
  explanation for an aggregate day correction.
- Search-result membership summaries and issue-membership Razor/htmx UI remain
  later reception vertical-slice work; they do not replace canonical query
  ownership.
- This review accepts Milestone 5 as complete for Milestone 6 handoff. It does
  not imply production or go-live readiness, and no adjustment command/UI has
  been introduced.

## Validation baseline after Step 87

The final validation result for this review is recorded in
`docs/implementation-progress.md` Step 87.

## Policy update after Step 85

ADR-014 and the synchronized domain/interaction/roadmap documents resolve the
Visit allocation and Freeze policy questions without adding Visit persistence
or changing the technical acceptance gaps that remained at that step.

## Calculation update after Step 86

The pure Memberships eligibility/source-fact boundary and its focused domain
tests close the required Visit-calculation gap.

## Adjustment rebuild update after Step 87

The controlled adjustment contract and PostgreSQL-backed cache rebuild close the
last Milestone 5 technical gate. Recalculation version 3 includes active
`visit_balance` and positive `extension_days` facts, preserves inactive history,
rejects unsupported active semantics and applies an explicit opening-state
recording-time cutover.
