---
name: bodylife-operations-production-readiness
description: Implementation guidance for BodyLife CRM operations and production readiness. Use when Codex implements or reviews structured logging, correlation IDs, PII masking, health checks, metrics, backup/restore runbooks, 30-day backup retention verification, restore rehearsal, owner restore-check, paper fallback, backdated reconciliation, production hardening, deployment checks, incident support, or go-live readiness.
---

# BodyLife Production Readiness

Use this skill for operations work that decides whether BodyLife CRM is safe to run as the business system of record.

## Start Here

Read `references/source-map.md`. Hosting provider is pending, so implement provider-neutral readiness until a provider is chosen.

## Operational Requirements

- Production must have provider-managed or supplemented automated backups with at least 30-day retention expectation.
- Production use waits for at least one restore rehearsal into isolated staging/test and owner restore-check acceptance.
- RPO target is preferably several hours/PITR and not worse than 24 hours; RTO target is same-business-day restore.
- Health checks and logs must make app downtime, command errors, recalculation failures, and backup failures visible.
- Technical logs are separate from business audit and must mask secrets and unnecessary PII.
- Paper fallback and backdated entries go through normal domain commands with `entry_origin`, `occurred_at`, server `recorded_at`, actor/session, reason/comment, validation, recalculation, and audit.

## Implementation Workflow

1. Add health check and structured logging with correlation ids.
2. Define PII/secrets logging policy in code/config/tests where possible.
3. Wire command outcomes to technical logs without copying business comments or sensitive data unnecessarily.
4. Prepare backup scope: PostgreSQL, migration version, app config needed for restore, and uploaded files if introduced.
5. Create restore runbook matching the actual deployment and migration process.
6. Execute and record restore rehearsal evidence before production use.
7. Add paper fallback template/process and entry batch support only through domain commands.
8. Run go-live checklist after full tests and restore readiness pass.

## Guardrails

- Do not treat a configured backup as sufficient until restore is rehearsed.
- Do not use app-level export/admin backup UI as v1 backup strategy.
- Do not rely on developer-only manual dumps as the primary backup path.
- Do not restore a whole database to fix one wrong visit/payment/freeze; use correction commands.
- Do not reconcile paper fallback with direct DB patches or forged `recorded_at`.
- Do not log passwords, tokens, session secrets, backup credentials, or unnecessary full personal data.
