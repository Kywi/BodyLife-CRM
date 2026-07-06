# Interaction Decision Checklist

Use this reference for component boundaries, APIs, workflows, and transaction design.

## Flows To Model

- Login and role-based entry.
- Reception search by card, name, full name, phone, last four digits.
- Client profile loading.
- New client creation.
- Membership issue with optional cash payment.
- Visit marking.
- Visit cancellation.
- Payment creation/correction.
- Freeze creation/cancellation.
- Non-working day creation/correction and membership extension.
- Daily report.
- Expiring/low-balance/negative/inactive-client reports.

## Boundary Questions

- Which layer owns membership calculations?
- Which commands must be atomic?
- Which commands create audit/history records?
- Which reads are ordinary entity reads vs report queries?
- Which workflows need confirmation, undo, or correction?
- Which actions are owner-only, admin-allowed, or unresolved?
- How are validation errors shown to reception staff?
- How are duplicate clicks, browser refreshes, and concurrent edits handled?

## Patterns To Compare

- Traditional client/server web app.
- Server-rendered app with server-side actions.
- SPA plus API.
- Desktop/local app with local database.
- LAN-hosted local server.
- Modular monolith.
- Event-assisted audit/history.
- Background jobs for reports/extensions.
- Offline-first or sync-based design, only if the requirement justifies it.

## Output Template

```markdown
## Interaction Model
- Shortlisted pattern(s):
- Recommended pattern, only if asked:
- Assumptions:

## Module Boundaries
| Module | Owns | Talks to | Does not own |
|---|---|---|---|

## Key Commands
| Command | Input | Transaction boundary | Audit record | Failure handling |
|---|---|---|---|---|

## Read Models / Reports
| View/report | Source data | Freshness | Notes |
|---|---|---|---|

## Tradeoffs
- ...

## Open Questions
- ...
```
