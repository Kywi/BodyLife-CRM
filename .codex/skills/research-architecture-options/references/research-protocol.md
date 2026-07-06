# Architecture Research Protocol

Use this reference when the user asks for a broad architecture investigation, comparison, or decision brief.

## Evidence Order

1. Local project evidence: graphify query, requirements docs, interview notes, existing code, current constraints.
2. Comparable systems: gym CRM, membership management, attendance tracking, cash-based SMB operations, studio/clinic admin, POS-light tools.
3. Current technical evidence: official docs, mature open-source implementations, vendor docs, release notes, pricing/service limits, operational guides.
4. Engineering judgment: explain assumptions and confidence.

## Questions To Answer

- What problem is the architecture solving in the first version?
- Which constraints come directly from BodyLife docs?
- Which requirements are architectural drivers rather than ordinary features?
- Which decisions are reversible, costly to reverse, or likely to constrain future work?
- Which similar systems solve the same class of problem, and what can be learned from them?
- Which options are intentionally excluded from the first version?
- What needs deeper validation before implementation?

## Decision Areas

- Product shape: single-user/local, LAN, web app, hosted SaaS-like app, hybrid, offline-first.
- Application architecture: modular monolith, layered architecture, feature modules, service boundaries, event-driven pieces.
- Runtime and language: ecosystem, local developer speed, hosting fit, maintainability, hiring/supportability.
- Data architecture: relational/document/local-first/event log, migrations, backups, search, audit, reporting.
- Interaction architecture: frontend/backend communication, API style, workflows, transaction boundaries, concurrency.
- Observability and operations: business audit, technical logs, metrics, tracing, backups, deployment, incident handling.
- Security and privacy: authentication, authorization, role boundaries, sensitive data, retention.

## Output Template

```markdown
# Architecture Research Brief

## Context From Current Docs
- ...

## Decision Drivers
| Driver | Why it matters | Evidence |
|---|---|---|

## Comparable Systems / Patterns
| System or pattern | Relevant lesson | Limits of comparison |
|---|---|---|

## Options
| Area | Option | Pros | Cons | Risks | Evidence needed |
|---|---|---|---|---|---|

## Cross-Cutting Concerns
- Data integrity:
- Audit/history:
- Search:
- Reporting:
- Migration:
- Backup/restore:
- Security:
- Operations:

## Recommendation Or Shortlist
- Recommended path, if asked:
- Confidence:
- Why this is not yet final:

## ADR Candidates
- ADR-001:
- ADR-002:

## Open Questions
- ...
```
