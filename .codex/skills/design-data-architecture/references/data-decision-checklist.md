# Data Architecture Checklist

Use this reference for schema, storage, migration, and reporting decisions.

## Domain Entities To Check

- Client.
- Card number.
- Membership type.
- Issued membership.
- Visit.
- Payment.
- Freeze.
- Non-working day or period.
- Extension history.
- Cancellation/correction record.
- User/operator.
- Technical client or quick visit model for one-time/trial visits.

## Invariants

- Card number is unique when present.
- Phone is normalized enough for reliable search and last-four lookup.
- A visit belongs to a client and usually to a membership.
- A payment belongs to a client and usually to a membership.
- Visit cancellation must restore/report the correct state and remain visible.
- Freeze and non-working day extensions must be traceable.
- Daily report must reflect corrections in a defined way.
- Negative lesson balance must preserve the first negative visit date when needed for next membership start.

## Storage Decisions

- Source of truth vs derived value.
- Snapshot vs calculation at read time.
- Transaction boundaries.
- Constraints and indexes.
- Audit/event table vs per-entity history tables.
- Soft delete vs immutable correction record.
- Import staging tables for Excel/paper migration.
- Retention and export requirements.

## Scenario Validation

For each candidate model, run these scenarios:

1. Client arrives with card and visit is marked.
2. Client arrives without card and is found by name or phone.
3. New membership is issued and cash payment is recorded.
4. Client goes negative and later buys a new membership.
5. Freeze extends membership.
6. Non-working day extends many memberships.
7. Visit is canceled after it created a negative balance.
8. Freeze or non-working day was entered by mistake.
9. Daily report is generated after payment correction.
10. Inactive-client report is generated from visit history.

## Output Template

```markdown
## Data Model
| Entity | Purpose | Key fields | Relationships | Notes |
|---|---|---|---|---|

## Invariants And Constraints
- ...

## Storage Options Compared
| Option | Pros | Cons | Operational risk | Fit |
|---|---|---|---|---|

## Shortlist Or Recommendation
- Shortlist:
- Recommended only if asked:
- Assumptions:
- What would change the choice:

## Reporting/Search Plan
- ...

## Migration And Backup Plan
- ...

## Open Questions
- ...
```
