# Support correlation lookup

Status: Milestone 10 operator runbook for successful audited commands.

This runbook connects role-controlled business audit to structured technical
request logs. It does not make technical logs a source of business truth and
does not introduce an in-app log viewer.

## Access boundary

- Owner/Admin may inspect the Audit Timeline and its collapsed Audit envelope.
- Only the technical operator/developer may access the configured log store.
- Share correlation ids and business record ids for investigation. Do not copy
  client names, phone numbers, comments, reasons, passwords, tokens, session
  secrets or connection strings into log searches or support messages.

## Lookup procedure

1. Find the successful business action in the Audit Timeline using the known
   client/entity, action and time window.
2. Confirm the action type, entity id, actor/session, occurred time and recorded
   time against the reported issue.
3. Expand `Audit envelope` and copy the exact `Request correlation ID`.
4. In the configured structured log store, filter by the exact
   `request_correlation_id` field and the correct `environment`.
5. Narrow by timestamp and inspect `route_or_command`, `method`, `status_code`,
   `duration_ms`, `outcome` and `error_class`. A redirecting successful command
   may have a `3xx` request outcome followed by a separate canonical reread.
6. Use technical symptoms to diagnose the request. Resolve business questions
   from canonical source records, membership state, reports and business audit.
7. Record the correlation id, time window, environment and technical conclusion
   in the incident note without copying sensitive business text from audit.

For local JSON console output, an exact structured-field lookup can be expressed
as:

```bash
jq -c --arg id 'support-correlation-id' \
  'select(.State.request_correlation_id == $id)' bodylife.log
```

The production query syntax and retention depend on the future hosting/logging
provider. The field name and investigation boundary remain provider-neutral.

## Interpretation rules

- A successful state-changing command should have a business audit row with the
  same `request_correlation_id` as its technical request outcome log.
- Business reasons/comments and unnecessary personal data belong in the
  role-controlled audit or source record, not in the technical request log.
- Several technical records may share a correlation id. Select the relevant
  route/command and method instead of assuming the first match is the command.
- If retained technical logs are unavailable, the business audit and canonical
  records remain authoritative. Do not reconstruct business history from logs.
- A missing audit row for a supposedly successful command is an audit-integrity
  incident. Do not create or edit an audit row manually.
