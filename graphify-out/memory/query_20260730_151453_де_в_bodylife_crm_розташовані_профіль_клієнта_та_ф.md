---
type: "codebase"
date: "2026-07-30T15:14:53.281718+00:00"
question: "Де в BodyLife CRM розташовані профіль клієнта та форми дій reception для відвідування, видачі абонемента, платежу і заморозки?"
contributor: "graphify"
outcome: "dead_end"
---

# Q: Де в BodyLife CRM розташовані профіль клієнта та форми дій reception для відвідування, видачі абонемента, платежу і заморозки?

## Answer

Scoped graph query surfaced only broad Workflow and skill nodes, not the concrete client profile fixture, Razor partials, or governing cash-only/freeze contracts. Direct source inspection of docs/ui-prototype/clients.html, docs/ui-prototype/assets/app.js, production Reception partials, interaction contracts, ADR-003, and accepted requirements was required.

## Outcome

- Signal: dead_end