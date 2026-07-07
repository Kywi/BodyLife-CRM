# BodyLife CRM operations design

Дата: 2026-07-07  
Статус: design draft for v1 implementation

Основа: `docs/adr/006-business-audit-corrections-and-technical-logs.md`, `docs/adr/009-backup-restore-and-operational-recovery.md`, `docs/adr/010-migration-manual-backfill-and-paper-fallback.md` і `docs/interaction-contracts.md`.

Цей документ описує stack-agnostic operational policy для BodyLife CRM v1. Він не обирає observability vendor, backup provider, hosting service або конкретний logging stack, бо deployment stack ще не зафіксований.

## 1. Operational goals

- Зберігати пояснювану бізнес-історію для візитів, оплат, абонементів, заморозок, неробочих днів, backdated entries і corrections.
- Розділяти business audit і technical logs: audit відповідає на питання "що сталося в бізнесі і хто це зробив", logs відповідають на питання "що сталося в системі".
- Не використовувати technical logs як джерело бізнес-правди або як заміну audit.
- Після кожного state-changing command мати узгоджений commit: source fact, recalculation, canonical read state і audit entry.
- Зробити backup реальним operational capability, а не припущенням: production-ready означає, що restore rehearsal уже виконаний і owner пройшов restore-check.
- Підтримати paper fallback при втраті інтернету або хостингу без synthetic fake history і без direct database edits.
- Забезпечити reconciliation після outage через звичайні domain commands, validation, recalculation і audit.
- Дати owner/admin/support людині зрозумілий workflow для виправлень, спорів і incident review.

Primary readers:

- Owner: перевіряє бізнес-історію, cash/visit totals, restore acceptance і sensitive corrections.
- Admin/Reception: виконує щоденні commands і первинно веде paper fallback.
- Technical operator/developer: підтримує logs, metrics, backups, restore і incident response.
- Future support person: розбирає скарги без прямого редагування production data.

## 2. Business audit

### Policy

Business audit у v1 є окремою append-only бізнес-історією. Він пишеться після успішних server-side commands/workflows через in-process hooks/events у межах того самого consistency boundary, де створюються source facts і recalculation results.

Audit entry не можна UPDATE/DELETE через application workflows. Corrections, cancellations і replacements не переписують минуле, а додають новий correction/cancellation entry з reason/comment і посиланням на оригінальний факт.

Audit має бути readable для owner і в обмеженому scope для admin. Audit не використовується для обчислення report totals, remaining visits, active status, effective end date або cash totals; ці значення читаються з canonical source records/read models.

### Required audit fields

| Field | Policy |
|---|---|
| `audit_entry_id` | Stable id для drill-down і support references. |
| `actor_account_id` | Хто виконав command або яка shared account session відповідальна. |
| `actor_role` | Role на момент command: Owner, Admin/Reception або інша майбутня роль. |
| `session_id` | Session/device accountability, особливо для shared Reception/Admin account. |
| `device_label` | Optional, якщо доступно. |
| `action_type` | Canonical event name з command matrix нижче. |
| `entity_type` / `entity_id` | Primary business entity. |
| `related_ids` | Client, membership, visit, payment, freeze, non-working period, report day або replacement/correction ids. |
| `entry_origin` | `normal`, `manual_backfill`, `paper_fallback` або майбутній `future_import`. |
| `occurred_at` | Фактичний час або business date події. |
| `recorded_at` | Server timestamp успішного commit. |
| `before_summary` / `after_summary` | До/після або domain-specific summary там, де це доречно. |
| `reason` / `comment` | Required для corrections, cancellations, card reassignment, backdated/fallback entries і owner-sensitive actions. |
| `request_correlation_id` | Зв'язок з technical logs/support investigation. |
| `idempotency_key` | Для quick actions і correction commands з ризиком duplicate submit, якщо command його приймав. |
| `changed_after_close` | Marker, якщо command змінив уже reconciled/closed business day. |

### Audit event matrix for commands

Queries/read actions не створюють business audit entries за замовчуванням. Вони можуть мати technical access logs, але не є бізнес-подіями, якщо майбутня owner policy явно не вимагатиме report-access auditing.

| Command | Audit event(s) | Required domain summary |
|---|---|---|
| `CreateClient` | `client.created` | Client identity summary, optional card assignment, duplicate warning acknowledgements. |
| `UpdateClient` | `client.updated` | Before/after identity, phone/status/comment changes, duplicate acknowledgement if any. |
| `AssignOrChangeCard` | `card.assigned`, `card.changed`, `card.cleared` | Old/new card summary, client id, reason for replacement or clearing. |
| `CreateMembershipType` | `membership_type.created` | Full catalog summary. |
| `EditMembershipType` | `membership_type.edited` | Before/after catalog fields and reason/comment for meaningful business change. |
| `DeactivateMembershipType` | `membership_type.deactivated` | Before/after active state and reason. |
| `IssueMembership` | `membership.issued`; optionally `payment.created`, `membership_negative_closure.created` | Issued snapshot, start date, optional payment, negative handling decision, opening/backfill state when present. |
| `MarkVisit` | `visit.marked` | Client, visit kind, membership/consumption, `occurred_at`, before/after membership summary, warning acknowledgement. |
| `CancelVisit` | `visit.canceled` | Original visit summary, reason, before/after membership summary, changed-after-close marker when relevant. |
| `CreatePayment` | `payment.created` | Amount, currency, cash context, client/membership link, `occurred_at`. |
| `CorrectPayment` | `payment.corrected`, `payment.canceled` | Original payment, replacement/cancellation summary, before/after amount/date/context, reason, changed-after-close marker. |
| `AddFreeze` | `freeze.added` | Membership, inclusive range, day count, reason, before/after effective end date summary. |
| `CancelFreeze` | `freeze.canceled` | Original freeze range, reason, before/after effective end date summary. |
| `AddNonWorkingDay` | `non_working_day.added` | Period, reason, affected membership count/summary, recalculation summary. |
| `CorrectNonWorkingDay` | `non_working_day.corrected`, `non_working_day.canceled` | Before/after period, old/new affected counts, reason, recalculation summary. |

### Backdated and correction audit rules

- Any `entry_origin` other than `normal` must be visible in owner/admin history where it matters.
- `occurred_at` and `recorded_at` must both be stored and displayed for backfilled/fallback entries.
- `manual_backfill` is allowed only through normal domain commands and only for valid active-client/opening-state scenarios described in ADR-010.
- `paper_fallback` entries must reference a paper batch id or line reference in `reason`/`comment`.
- Corrections after a reconciled/closed day require the command result and audit entry to carry a changed-after-close marker.
- Direct database edits, synthetic fake history and unmarked backdated entries are outside the application contract.

### Retention and access

- Business audit is retained with the business records it explains.
- Audit should remain exportable or inspectable by technical operator/developer for support, but owner/admin UI must hide technical noise.
- If future privacy retention rules require removing personal display data, the system should preserve audit integrity, ids, action type, timestamps and reason markers while applying the chosen anonymization/redaction policy.

## 3. Technical logs

### Policy

Technical logs are structured runtime/debugging records. They are not business history and must not be the only place where visits, payments, freezes, non-working days, backdated entries or corrections can be explained.

Minimum log fields for request/command handling:

| Field | Policy |
|---|---|
| `timestamp` | Server-side time. |
| `level` | `info`, `warn`, `error`; `debug` only when explicitly enabled. |
| `environment` | Local/dev, staging/test or production. |
| `request_correlation_id` | Same id that appears in audit when a command succeeds. |
| `actor_account_id` | Allowed for accountability; avoid unnecessary personal details. |
| `actor_role` | Useful for permission and support debugging. |
| `session_id` | Include when useful; avoid logging secrets/tokens. |
| `route_or_command` | Route/query/command name. |
| `entity_refs` | Ids only where useful for debugging; avoid raw personal data. |
| `duration_ms` | Request/command/report latency. |
| `outcome` | Success, validation error, permission denied, conflict, system error. |
| `error_class` | Stable class/category, not raw stack traces in user-visible contexts. |
| `job_name` | For backup/restore/recalculation/background operations when applicable. |

Sensitive data policy:

- Do not log passwords, tokens, session secrets or backup credentials.
- Mask phone numbers and avoid logging full names unless there is a clear support need and retention is defined.
- Do not log raw comments/reasons by default if they may contain personal or sensitive details; keep them in business records/audit where access is role-controlled.
- Production debug logs are disabled by default and enabled only for time-boxed troubleshooting.
- Logs may reference `request_correlation_id` and business entity ids so support can navigate from a user report to technical symptoms without copying personal data into logs.

Retention/access policy:

- Production technical logs should have short operational retention appropriate for debugging and incident review.
- Error and backup/restore status logs may need longer retention than verbose request logs.
- Local/dev logs may be more verbose but must not be copied into shared channels with production personal data.
- Access is limited to technical operator/developer and, where needed, owner-facing status summaries.

### Metrics

Metrics should be stack-agnostic signals that can later be implemented through the chosen deployment/runtime. They should favor operational decisions over dashboard volume.

| Signal | Why it matters | Initial threshold / response |
|---|---|---|
| App availability / health check | Reception needs the app during business hours. | Investigate immediately if unavailable during working hours. |
| Request/command error rate | Detects broken workflows and validation/concurrency regressions. | Review if errors spike or repeated system errors occur. |
| Command latency | Slow quick actions block reception. | Review slow `MarkVisit`, `CreatePayment`, `IssueMembership`, `AddFreeze`. |
| Report latency | Daily report and drill-downs must remain usable. | Review slow `GenerateDailyReport` and membership lists. |
| `MarkVisit` count | Baseline activity and detects outage gaps. | Compare against expected day activity and paper fallback batches. |
| `CreatePayment` count and daily cash sum | Detects cash/report mismatch early. | Reconcile with daily report and cash drawer process. |
| Failed login count | Detects auth mistakes or suspicious access. | Review repeated failures, especially owner account. |
| Permission denied count for owner-only actions | Detects role misconfiguration or attempted sensitive actions. | Review repeated denials. |
| Duplicate submission / idempotency hits | Detects double taps/scans and UI retry behavior. | Review repeated hits on quick actions. |
| Recalculation failures | Membership state cannot be trusted after failed commands. | Treat as blocking incident for affected workflow. |
| Backup success/failure | Backup must be observable. | Failed or missing backup status requires same-day review. |
| Last successful restore rehearsal date | Backup is not trusted until restore is tested. | Production readiness fails if no successful rehearsal is recorded. |
| Paper fallback batch count/open batches | Prevents unentered paper records from being forgotten. | Owner/admin review until every batch is reconciled. |

Alerting/monitoring implementation is deferred until deployment is chosen. The policy requirement is that production has at least one reliable way to notice app unavailability, backup failures and repeated command/system errors.

## 4. Backup/restore

### Backup strategy

BodyLife CRM v1 uses hosting/provider-managed automated backups as the primary backup mechanism. The exact provider/tool is intentionally not chosen here.

Required backup scope:

- Primary transactional database.
- Uploaded/imported files, if v1 adds any.
- Application configuration needed for restore, excluding secrets from ordinary logs/docs.
- Migration/schema version.
- Enough deployment metadata for a technical operator/developer to recreate the application environment.

Accepted v1 policy:

- Backup owner: hosting/provider plus technical operator/developer.
- Restore owner: technical operator/developer.
- Restore acceptance: owner completes restore-check checklist.
- Automated backup retention: minimum 30 days.
- RPO target: prefer several hours or point-in-time recovery when the chosen stack supports it; not worse than 24 hours.
- RTO target: same-business-day restore for a production incident.
- App-level export UI is not part of v1 scope.
- Developer-only manual dumps are not the primary backup mechanism.

### Restore rehearsal

Restore rehearsal is mandatory before production use and repeated periodically after production launch. It should also be repeated after material changes to hosting, database, backup configuration, file storage, migration strategy or restore runbook.

Restore rehearsal must never overwrite production. It restores a selected backup into an isolated staging/test environment or equivalent non-production target.

Minimum evidence to record:

- Rehearsal date/time.
- Backup snapshot timestamp.
- Person performing restore.
- Environment restored into.
- Schema/migration version after restore.
- RPO observed: difference between incident/snapshot target and latest restored business record.
- RTO observed: elapsed time from restore start to owner acceptance.
- Owner restore-check result.
- Issues found and follow-up actions.

### Restore-check procedure

The restore-check is the owner-visible acceptance procedure that proves a restored environment is useful for the business.

1. Technical operator/developer selects a backup snapshot and records its timestamp.
2. Technical operator/developer restores database, files if any, configuration and migration version into an isolated staging/test environment.
3. Technical operator/developer confirms the restored app starts, login works and no production writes are pointed at the test environment.
4. Technical operator/developer verifies technical counts: clients, issued memberships, visits, payments, freezes, non-working periods and audit entries are present and plausible for the snapshot time.
5. Owner logs into the restored environment with a safe test/staging route.
6. Owner searches for at least one known client by card/name/phone and opens the client profile.
7. Owner checks that current membership state, remaining visits, negative warning if applicable, payment history, visit history and freeze/non-working explanations look plausible for the backup timestamp.
8. Owner opens the daily report for a known recent business day and checks visit count, payment count, cash sum, corrections/cancellations and drill-down links.
9. Owner opens audit/history for at least one recent command and confirms actor/session, `occurred_at`, `recorded_at`, action type and reason/comment are readable.
10. If the backup included paper fallback or manual backfill records, owner verifies that `entry_origin`, paper batch/source comment, `occurred_at` and `recorded_at` are visible.
11. Technical operator/developer records restore-check pass/fail, observed RPO/RTO and any discrepancies.
12. Production readiness passes only if restore-check passes or all blocking discrepancies are corrected and the rehearsal is repeated.

### Restore during production incident

- Decide whether the incident is data loss/corruption, app outage, hosting outage or user error requiring correction.
- Prefer correction commands for isolated business mistakes; do not restore the whole database to fix a single mistaken visit/payment/freeze.
- For real data loss/corruption, identify target restore time and expected RPO.
- Freeze production writes during restore decision if partial writes could worsen inconsistency.
- Communicate to owner/admin that paper fallback starts if the app is unavailable.
- Restore into production only through the chosen hosting/provider restore process and after owner/technical approval.
- After restore, run restore-check essentials, then reconcile any paper fallback records or business events that occurred after the restored snapshot.

## 5. Paper fallback and backdated entries

### When paper fallback starts

Paper fallback starts when reception cannot reliably use the hosted app because of internet outage, device/browser failure, hosting outage, auth outage or a blocking production incident.

Owner/admin should record the outage start time as soon as practical. If the app is partially available but commands are failing, stop state-changing work in the app and use paper until the system is healthy again.

### Paper fallback rules

- Use a numbered paper batch per outage/business day.
- Record every row with a stable line number.
- Keep the original paper sheet until the batch is entered, reconciled and accepted.
- Record actual business time/date as `occurred_at`, not the later data-entry time.
- Record who wrote the row on paper and who later entered it into the system.
- Use normal domain commands after recovery; never direct database edits.
- Set `entry_origin = paper_fallback` for every entered row.
- Put paper batch id and line number in reason/comment.
- Require reason/comment for every fallback entry, even if the command would not require it for a normal current-day entry.
- Preserve validation rules, warning acknowledgements, permissions, idempotency and recalculation.
- If the fallback row affects a closed/reconciled day, apply the owner/changed-after-close policy.
- Do not create synthetic fake history to make the paper period look like normal online operation.

Minimum paper row fields:

| Field | Applies to |
|---|---|
| Batch id and line number | All fallback rows. |
| Written by | All fallback rows. |
| Client name and, if known, card/phone | Visits, payments, freezes, memberships. |
| Business event type | Visit, payment, freeze, membership issue, correction/cancellation note. |
| `occurred_at` / business date and time | All fallback rows. |
| Amount/currency/payment context | Payments. |
| Membership or visit kind | Visits and membership-related payments. |
| Freeze start/end and reason | Freezes. |
| Membership type/start date/opening state source | Membership issue/backfill. |
| Cash handoff note | Payments when cash drawer reconciliation needs it. |
| Comment/reason/source | All fallback rows. |

### Backdated and manual backfill rules

- `manual_backfill` is for starting v1 with active clients/active memberships when needed, not for full migration from Excel/paper.
- Full import/migration from Excel or paper is outside v1.
- Active membership opening state must be explicit source fact: start date, membership type or snapshot, current remaining/negative visits, known end/extension state, reason and source.
- Backdated visits/payments/freezes/memberships use ordinary commands with `occurred_at`, validation, recalculation and audit.
- `recorded_at` is always server commit time and must not be forged to match `occurred_at`.
- Future import, if added, must go through staging, validation, commands and audit.

### Reconciliation after outage

Reconciliation is the process that turns paper fallback into trustworthy system state.

1. Owner/admin creates or labels the paper batch with outage date/time range.
2. Admin enters rows through normal commands using `entry_origin = paper_fallback`, actual `occurred_at` and paper batch/line reason.
3. Commands that produce warnings still require acknowledgements; commands that affect closed/reconciled days follow owner policy.
4. After entry, owner/admin runs `GenerateDailyReport` for every affected business date.
5. Compare report visit count, payment count and cash sum against the paper batch and cash drawer notes.
6. Open drill-down rows for fallback entries and confirm the audit timeline shows `paper_fallback`, actor/session, `occurred_at`, `recorded_at` and paper batch reference.
7. Check affected client profiles for membership state, remaining visits, negative balances and freeze/non-working extension explanations.
8. Resolve mismatches only through correction/cancellation commands with reason/comment; do not edit rows directly.
9. Mark discrepancies that changed an already reconciled day with changed-after-close markers.
10. Owner accepts the batch when paper rows, daily reports, cash totals and audit entries match.
11. Keep the paper batch according to the business retention policy or until owner explicitly approves disposal under the future records policy.

## 6. Support and correction workflow

### Support triage

Support starts by classifying the report:

| Type | Examples | Source of truth |
|---|---|---|
| Business dispute | "Visit count is wrong", "cash total changed", "freeze did not extend membership". | Canonical source records, membership state, daily report, business audit. |
| User/input mistake | Wrong visit, wrong payment amount, wrong freeze range, wrong client/card. | Original source fact plus correction command and audit. |
| Technical incident | App unavailable, command error, slow report, backup failure. | Technical logs, metrics, health status, restore/incident records. |
| Data recovery incident | Lost/corrupt data or restore needed. | Backup/restore runbook and owner restore-check. |
| Paper fallback batch | Records entered after outage do not match paper/cash. | Paper batch, fallback audit entries, daily report. |

### Investigation steps

1. Capture who reported the issue, when, affected date, client/card if known, command/action and expected vs actual result.
2. Use UI queries first: client profile, history, audit timeline and daily report drill-down.
3. Use `request_correlation_id` from audit or command result to inspect technical logs when needed.
4. Do not treat logs as business truth if audit/source records disagree.
5. Identify whether the fix is a normal correction command, owner-only correction, restore incident or paper reconciliation.
6. For corrections, require reason/comment and show expected affected membership/report consequences before commit where the command contract supports it.
7. After correction, reread canonical profile/report state and verify audit/history shows original plus correction/cancellation.
8. If a day changed after reconciliation, confirm changed-after-close labels are visible.

### Correction rules

- Wrong visit: use `CancelVisit`; never delete the visit row silently.
- Wrong payment: use `CorrectPayment` with replace/cancel mode; daily report totals update through canonical payment status/replacement rows.
- Wrong freeze: use `CancelFreeze`; add a new correct freeze if needed.
- Wrong non-working day: owner uses `CorrectNonWorkingDay` with affected-scope preview/confirmation.
- Wrong card assignment: use `AssignOrChangeCard` with reason/comment.
- Wrong client identity/contact: use `UpdateClient`; do not merge silently.
- Backdated/fallback mistake: correct using the same correction commands, preserving original `entry_origin` and paper/manual source explanation in history.
- Isolated business mistakes should not trigger database restore.

### Escalation

- Escalate to owner for owner-only actions, closed/reconciled day changes, non-working day changes and ambiguous cash/payment disputes.
- Escalate to technical operator/developer for repeated system errors, recalculation failures, backup failures, restore decisions or suspected data corruption.
- Start paper fallback during unresolved availability incidents that block reception work.

## 7. Production readiness checklist

Business audit:

- [ ] Separate append-only audit model/table exists.
- [ ] Audit writes happen only after successful commands/workflows and are committed consistently with source facts.
- [ ] Audit matrix events are implemented for all state-changing commands listed in this document.
- [ ] `occurred_at`, `recorded_at`, `entry_origin`, actor/account, role, session/device, reason/comment and correlation id are stored.
- [ ] Corrections/cancellations create new entries and do not rewrite past audit.
- [ ] Owner/admin can inspect audit/history without technical log noise.

Technical logs and metrics:

- [ ] Structured logs include request correlation id, route/command, duration, outcome and error class.
- [ ] Sensitive data is masked or omitted according to the logging policy.
- [ ] Production debug logging is off by default.
- [ ] Metrics/health signals exist for availability, command errors, slow reports, failed logins, recalculation failures and backup status.
- [ ] There is a defined way to notice app downtime and backup failure before or during business hours.

Backup/restore:

- [ ] Provider-managed automated backups are enabled for the full backup scope.
- [ ] Minimum 30-day backup retention is configured.
- [ ] RPO/RTO expectations are documented for the chosen deployment.
- [ ] Restore runbook exists and matches the actual deployment.
- [ ] At least one pre-production restore rehearsal has passed.
- [ ] Owner completed and accepted the restore-check procedure.
- [ ] Last successful restore rehearsal date is recorded.
- [ ] Restore does not depend on developer-only manual dumps as the primary backup path.

Paper fallback and reconciliation:

- [ ] Paper fallback template exists with batch id, line number, client/card, event type, `occurred_at`, amount/range/source and reason/comment fields.
- [ ] Staff know when to stop online commands and switch to paper.
- [ ] Fallback entry workflow sets `entry_origin = paper_fallback`.
- [ ] Backdated/fallback entries require reason/comment and preserve `occurred_at` vs `recorded_at`.
- [ ] Reconciliation process compares paper rows, daily report totals, cash totals and audit entries.
- [ ] Changed-after-close markers are shown when fallback/corrections affect reconciled days.

Support/corrections:

- [ ] Support workflow distinguishes business disputes, user mistakes, technical incidents, restore incidents and paper fallback batches.
- [ ] Correction commands exist for visits, payments, freezes and non-working days.
- [ ] Owner-only policy is enforced for sensitive/closed-day/non-working-day corrections.
- [ ] Idempotency keys or duplicate-submit guards exist for quick actions and correction commands with duplicate risk.
- [ ] UI rereads canonical state after successful commands and does not preserve optimistic business calculations.

Security/access:

- [ ] Role checks match command contracts.
- [ ] Shared Reception/Admin accountability includes session/device context in audit.
- [ ] Logs do not contain secrets, tokens or unnecessary personal data.
- [ ] Backup/restore access is limited to the technical operator/developer and chosen provider controls.

## 8. Risks

- Deployment stack is not chosen yet, so exact backup feature set, restore mechanism, uptime monitoring and log retention implementation remain open.
- Provider-managed backup can create false confidence if restore rehearsal is skipped or owner restore-check is not repeated after infrastructure changes.
- No app-level export UI in v1 means restore and provider backup discipline are especially important.
- Paper fallback depends on human discipline; missing batch ids, unclear handwriting or late entry can create reconciliation gaps.
- Shared Reception/Admin account can weaken accountability unless session/device labels and operating procedures are clear.
- Backdated entries are necessary for fallback/backfill but can erode trust if `entry_origin`, `occurred_at`, `recorded_at` and reason are not visible.
- Corrections after reconciled days can surprise owner/admin unless changed-after-close markers are prominent in daily report and history.
- Technical logs may leak personal data if masking rules are not enforced early.
- Recalculation failures are high-risk because reports, profile state and audit may otherwise appear partially updated; affected commands must fail clearly rather than pretending success.
- Day close/reconciliation is referenced by policy but not yet defined as an explicit v1 command; if the product needs formal day closing, add a dedicated command/ADR before implementation.
