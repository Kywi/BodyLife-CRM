---
name: research-architecture-options
description: Neutral architecture research for BodyLife CRM and similar small-business CRM, gym, studio, membership, booking, or operations systems. Use when Codex needs to investigate architecture before implementation, compare similar projects and existing approaches, identify decision drivers, explore pros/cons, or prepare architecture options for language, database, deployment, component interaction, logging, audit, observability, security, migration, reporting, or other system design choices.
---

# Research Architecture Options

## Overview

Use this skill to turn BodyLife CRM requirements into an evidence-based architecture research brief. Keep the work approach-neutral: investigate the problem, comparable systems, constraints, options, tradeoffs, risks, and open questions before recommending anything.

## Workflow

1. Start from project evidence.
   - If `graphify-out/graph.json` exists, run `graphify query "<architecture question>" --budget 2500` before raw browsing.
   - Read only the relevant sections from `docs/first-version-requirements.md`, `docs/initial-context.txt`, and `docs/question-answering-interview.txt`.
   - Extract business flows, entities, roles, reports, audit/history needs, edge cases, and unresolved questions.

2. Frame the research question.
   - Convert the user's request into concrete decision areas: language/runtime, backend style, frontend style, database, hosting, data migration, module boundaries, API style, logging/audit, backup, monitoring, security, access control, reporting, and operations.
   - Separate known requirements from assumptions.
   - Name the decision horizon: first working version, near-term growth, or long-term platform.

3. Research comparable systems and existing approaches.
   - Look at adjacent domains: gym CRM, membership management, small clinic/studio admin tools, POS-light systems, appointment/attendance systems, and small-business admin dashboards.
   - When the answer depends on current versions, pricing, service limits, ecosystem health, compliance posture, or cloud/vendor features, browse current primary sources and cite them.
   - Prefer official documentation, architecture writeups, technical case studies, and mature open-source projects over generic opinions.

4. Compare options without prematurely choosing.
   - Include at least three plausible options for important decisions unless the project constraints make that unrealistic.
   - Evaluate each option against BodyLife-specific criteria: fast reception workflow, simple administration, reliable membership calculations, cash payment visibility, auditability, data integrity, search speed, low maintenance burden, backup/restore simplicity, migration from paper/Excel, and future extensibility.
   - State what each option makes easy, what it makes hard, what it risks, and what evidence would change the conclusion.

5. Produce a decision-ready artifact.
   - Use `references/research-protocol.md` for the output structure.
   - Include sources, assumptions, constraints, option matrix, risks, open questions, and ADR candidates.
   - If recommending a path, label it as a recommendation with confidence and rationale, not as an unquestioned default.

## BodyLife Anchors

- First version replaces paper and partial Excel accounting for memberships.
- Core chain: client -> card/search -> membership -> visit -> remaining lessons or negative balance -> freezes/non-working days -> cash payment -> history -> daily report -> inactive-client control.
- First version excludes mobile app, online payments, bank/terminal integrations, turnstiles, barcode scanning, complex accounting, stock, bonuses, and trainer-specific accounting.
- Owner and administrator are the primary roles; trainer-specific access is unresolved.
- History and transparency matter because disputed visits, payments, freezes, cancellations, and extensions must be explainable.

## Guardrails

- Do not choose a language, database, framework, or cloud provider because it is familiar.
- Do not treat current business requirements as a technical schema without modeling invariants and edge cases.
- Do not collapse business audit history into ordinary application logs.
- Do not overfit the first version in a way that blocks likely next steps, but do not design a large enterprise platform without evidence.
- Prefer boring, understandable operations when two options are close.
