# Business Audit Foundation Inventory

Date: 2026-07-20

Status: Milestone 10 foundation inventory and executable command-matrix gate
completed. The first identified append-only policy gap is addressed by
`20260720100603_HardenBusinessAuditAppendOnly`.

## Scope and sources

This inventory maps the implemented state-changing workflows to the accepted
audit contract before audit history queries or UI are added. Its governing
sources are ADR-006, `docs/operations-design.md`, `docs/data-architecture.md`,
`docs/interaction-contracts.md` and Milestone 10 in
`docs/implementation-roadmap.md`.

Authentication attempts and session expiry are security/technical lifecycle
events rather than business mutations, so they remain in structured technical
logs. Owner-managed staff account and credential changes are business settings
mutations and therefore do create business audit entries.

## Schema mapping

| Accepted audit field | PostgreSQL representation | Status |
|---|---|---|
| `audit_entry_id` | `id uuid` primary key | Present. |
| actor identity and role | `actor_account_id`, `actor_account_type`, `actor_role` | Present. Account type preserves named versus shared accountability. |
| session and device | `session_id`, nullable `device_label` | Present. |
| action and primary entity | `action_type`, `entity_type`, `entity_id` | Present with non-empty action/entity checks. |
| related ids | `related_entity_refs jsonb` | Present. Query shape and indexing must be validated with the first client-history query. |
| entry origin | `entry_origin` | Present with the four accepted-value check. |
| event and recording times | `occurred_at`, `recorded_at` | Present. |
| before/after summary | `before_summary jsonb`, `after_summary jsonb` | Present and non-null. |
| reason and comment | nullable `reason`, nullable `comment` | Present. Context-dependent requiredness is enforced by commands. |
| request correlation | `request_correlation_id` | Present with a non-empty check. |
| duplicate-submit identity | nullable `idempotency_key` | Present. |
| reconciliation marker | `changed_after_close` | Present. |

`BusinessAuditAppender` is the single persistence mapper. Successful command
paths add the audit row through the same `BodyLifeDbContext` transaction as
their source facts, recalculation and idempotency result.

## Implemented command matrix

| State-changing workflow | Canonical audit event(s) | Persistence path and focused PostgreSQL evidence |
|---|---|---|
| `CreateClient` | `client.created` | `CreateClientCommandHandler`; `PostgreSqlCreateClientCommandTests`. |
| `UpdateClient` | `client.updated` | `UpdateClientCommandHandler`; `PostgreSqlUpdateClientCommandTests`. |
| `AssignOrChangeCard` | `card.assigned`, `card.changed`, `card.cleared` | `AssignOrChangeCardCommandHandler`; `PostgreSqlAssignOrChangeCardCommandTests`. |
| `CreateMembershipType` | `membership_type.created` | `CreateMembershipTypeCommandHandler`; `PostgreSqlCreateMembershipTypeCommandTests`. |
| `EditMembershipType` | `membership_type.edited` | `EditMembershipTypeCommandHandler`; `PostgreSqlEditMembershipTypeCommandTests`. |
| `DeactivateMembershipType` | `membership_type.deactivated` | `DeactivateMembershipTypeCommandHandler`; `PostgreSqlDeactivateMembershipTypeCommandTests`. |
| `IssueMembership` | `membership.issued`; `payment.created` when payment is captured | `IssueMembershipCommandHandler` and `MembershipIssuePaymentWriter`; `PostgreSqlIssueMembershipCommandTests`. Negative handling remains part of the issued summary; there is no separate implemented closure command. |
| `CreateMembershipOpeningState` | `membership_opening_state.created` | ADR-010 backfill extension in `CreateMembershipOpeningStateCommandHandler`; `PostgreSqlCreateMembershipOpeningStateCommandTests`. |
| `MarkVisit` | `visit.marked` | `MarkVisitCommandHandler`; `PostgreSqlMarkVisitCommandTests`. |
| `CancelVisit` | `visit.canceled` | `CancelVisitCommandHandler`; cancellation cases are covered by `PostgreSqlMarkVisitCommandTests`. |
| `CreatePayment` | `payment.created` | `CreatePaymentCommandHandler`; `PostgreSqlCreatePaymentCommandTests`. |
| `CorrectPayment` | `payment.corrected`, `payment.canceled` | `CorrectPaymentCommandHandler`; `PostgreSqlCorrectPaymentCommandTests`. |
| `AddFreeze` | `freeze.added` | `AddFreezeCommandHandler`; `PostgreSqlAddFreezeCommandTests`. |
| `CancelFreeze` | `freeze.canceled` | `CancelFreezeCommandHandler`; `PostgreSqlCancelFreezeCommandTests`. |
| `AddNonWorkingDay` | `non_working_day.added` | `AddNonWorkingDayCommandHandler`; `PostgreSqlAddNonWorkingDayCommandTests`. |
| `CorrectNonWorkingDay` | `non_working_day.corrected`, `non_working_day.canceled` | `CorrectNonWorkingDayCommandHandler`; `PostgreSqlCorrectNonWorkingDayCommandTests`. |
| `CreateStaffAccount` | `staff_account.created` | `StaffAccountLifecycleService`; `PostgreSqlStaffAccountLifecycleTests` and `PostgreSqlStaffAccountAuditTests`. |
| `UpdateStaffAccountDisplayName` | `staff_account.display_name_updated` | `StaffAccountLifecycleService`; staff lifecycle/audit suites. |
| `SetStaffAccountActiveState` | `staff_account.activated`, `staff_account.deactivated` | `StaffAccountLifecycleService`; staff lifecycle/audit suites. |
| `SetStaffCredentials` | `staff_credentials.configured`, `staff_credentials.reset` | `StaffCredentialsService`; `PostgreSqlStaffCredentialsTests` and `PostgreSqlStaffAccountAuditTests`. |

## Append-only hardening

Before this inventory, application code only inserted audit rows, but the
database still accepted direct `UPDATE` and `DELETE` statements. Migration
`20260720100603_HardenBusinessAuditAppendOnly` adds a PostgreSQL statement
trigger that rejects both operations. Its down migration removes the trigger
and function in dependency order.

`PostgreSqlBusinessAuditAppendOnlyTests` proves that a valid insert succeeds,
both mutation forms fail with the dedicated PostgreSQL error, and the original
row remains unchanged. Corrections and cancellations must continue to append
new source and audit facts.

## Executable command-matrix gate

`BusinessAuditEventMatrix` registers all 26 canonical action variants emitted
by the 20 implemented state-changing workflows. The shared
`BusinessAuditAppender` now rejects an unregistered action, the wrong primary
entity type, a missing command-specific related/before/after payload, a missing
idempotency key where the command requires one, or a missing explanation where
the accepted contract requires a reason or comment.

The appender also rejects empty actor account/session ids, blank request
correlation ids, a missing server-recorded time, and non-normal entries without
both an explicit `occurred_at` and a reason or comment. This closes the generic
envelope-validation gap that previously depended entirely on each command's
local validator.

`BusinessAuditMatrixTests` compares every declared `*AuditActions` constant
with the executable matrix and proves rejection happens before EF tracking.
`PostgreSqlBusinessAuditMatrixTests` persists every canonical event and checks
the complete shared-account/session/device, time, origin, explanation,
correlation, idempotency, related-reference, summary and changed-after-close
contract. The complete PostgreSQL command suite passes through the same
appender, so command-specific success paths cannot bypass this gate.

## Remaining Milestone 10 work

- Establish and test the client-history lookup shape over primary and related
  entity references, including its PostgreSQL index plan.
- Implement `GetClientHistory`, then owner/admin `GetAuditTimeline` with the
  accepted visibility rules and stable newest-first ordering.
- Add profile/report correlation links and tablet/phone history UI only after
  the query contracts are proven.
