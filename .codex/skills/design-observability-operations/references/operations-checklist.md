# Observability And Operations Checklist

Use this reference for logging, audit, metrics, backups, deployment, and support design.

## Business Audit History

Capture for important actions:

- Actor user id and role.
- Action type.
- Entity type and entity id.
- Before/after or domain-specific summary when appropriate.
- Timestamp.
- Reason/comment for cancellations or corrections when useful.
- Correlation id or request id if available.

Important actions include:

- Client created/edited.
- Card number changed.
- Membership issued/canceled/finished.
- Visit marked/canceled.
- Payment recorded/corrected/canceled.
- Freeze added/canceled.
- Non-working day added/corrected/canceled.
- Membership extension applied.
- Settings/membership type changed.

## Technical Logs

- Use structured logs where practical.
- Include request id, actor id, route/command, duration, outcome, and error class.
- Mask phone numbers and avoid logging unnecessary personal data.
- Keep debug logs out of production unless explicitly enabled.
- Define retention and export policy.

## Metrics

Minimum useful metrics:

- App availability.
- Request/command errors.
- Slow requests or slow reports.
- Visit marking count.
- Payment recording count.
- Failed login count.
- Backup success/failure.
- Last successful restore test date.

## Backup And Restore

- Define backup frequency.
- Define backup storage location and access.
- Define retention.
- Define restore steps.
- Test restore before trusting the system.
- Include database, uploaded/imported files if any, environment/config, and migration version.

## Output Template

```markdown
## Operational Goals
- ...

## Business Audit Policy
| Action | Audit fields | Visible to owner? | Retention |
|---|---|---|---|

## Technical Logging Policy
- Levels:
- Sensitive data:
- Retention:
- Access:

## Metrics / Monitoring
| Signal | Why it matters | Alert threshold |
|---|---|---|

## Backup / Restore
- Frequency:
- Location:
- Restore test:

## Options Compared
| Option | Pros | Cons | Cost/complexity | Fit |
|---|---|---|---|---|

## Shortlist Or Recommendation
- Stack-agnostic policy:
- Tool/vendor shortlist:
- Recommended only if deployment model is known:
- What would change the choice:

## Open Questions
- ...
```
