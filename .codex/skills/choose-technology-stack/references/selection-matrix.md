# Stack Selection Matrix

Use this reference when comparing languages, frameworks, databases, hosting, and supporting tools.

## Required Inputs

- Current BodyLife requirements and exclusions.
- Expected number of users, devices, and simultaneous sessions.
- Hosting preference or constraint: local, LAN, cloud, managed, self-hosted, undecided.
- Data sensitivity and backup/restore expectations.
- Migration needs from paper or Excel.
- Developer/team constraints and long-term maintenance owner.

## Criteria

| Criterion | What to examine |
|---|---|
| Domain fit | Memberships, visits, payments, freezes, reports, history |
| Data integrity | Transactions, constraints, migrations, concurrency |
| Auditability | Business history separate from technical logs |
| Simplicity | Build, deploy, operate, explain, recover |
| Developer speed | Tooling, tests, debugging, ecosystem |
| UI speed | Fast search and reception workflow |
| Reporting | Daily cash, expiring memberships, low balance, inactive clients |
| Search | Card number, name, phone, last four digits |
| Future growth | Online payments, barcode/NFC, mobile, integrations |
| Cost | Hosting, database, backups, monitoring, maintenance |
| Risk | Vendor lock-in, ecosystem churn, operational fragility |

## Output Matrix

```markdown
| Option | Fit | Pros | Cons | Hidden costs | Reversal cost | Confidence |
|---|---:|---|---|---|---|---|
```

Use qualitative scores such as High/Medium/Low unless the user explicitly asks for numeric weights.

## Decision Record

```markdown
## Decision
Selected/shortlisted:

## Why
- ...

## Alternatives Considered
- ...

## Consequences
- Positive:
- Negative:
- Risks:

## Validation Before Build
- ...
```
