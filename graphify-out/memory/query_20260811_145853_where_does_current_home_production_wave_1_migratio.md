---
type: "query"
date: "2026-08-11T14:58:53.005921+00:00"
question: "Where does current-Home production Wave 1 migration connect the shared Razor shell, Home queries, responsive navigation, and Playwright acceptance?"
contributor: "graphify"
outcome: "corrected"
correction: "The verified path is docs/ui-visual-fidelity-migration-plan.md and coverage matrix -> Pages/Shared/_Layout.cshtml, _AppNavigation.cshtml, _CurrentSession.cshtml -> Pages/Index.cshtml and its three server-owned queries -> wwwroot/css/production-shell.css plus site.js -> PostgreSQL-backed ReceptionHomeSmokeTests, ReceptionHomeStateSmokeTests and UiStyleCoverageSmokeTests. Static prototype approval authorizes the target, while the real Razor tablet/phone captures still require explicit product-owner approval."
source_nodes: ["_AppNavigation.cshtml", "_CurrentSession.cshtml", "IBodyLifeRequestContextResolver"]
---

# Q: Where does current-Home production Wave 1 migration connect the shared Razor shell, Home queries, responsive navigation, and Playwright acceptance?

## Answer

The generic traversal surfaced _AppNavigation and _CurrentSession but collided with the EF Migration node and omitted the actual Wave 1 composition and acceptance path.

## Outcome

- Signal: corrected
- Correction: The verified path is docs/ui-visual-fidelity-migration-plan.md and coverage matrix -> Pages/Shared/_Layout.cshtml, _AppNavigation.cshtml, _CurrentSession.cshtml -> Pages/Index.cshtml and its three server-owned queries -> wwwroot/css/production-shell.css plus site.js -> PostgreSQL-backed ReceptionHomeSmokeTests, ReceptionHomeStateSmokeTests and UiStyleCoverageSmokeTests. Static prototype approval authorizes the target, while the real Razor tablet/phone captures still require explicit product-owner approval.

## Source Nodes

- _AppNavigation.cshtml
- _CurrentSession.cshtml
- IBodyLifeRequestContextResolver