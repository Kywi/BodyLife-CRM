---
type: "query"
date: "2026-07-30T10:41:38.913358+00:00"
question: "Де в GitHub Pages шаблоні BodyLife дублюються акаунт, пристрій, ID сесії та завершення сеансу, і як їх консолідувати?"
contributor: "graphify"
outcome: "dead_end"
source_nodes: ["Reception", "Index.cshtml", "_ReceptionWorkspace.cshtml"]
---

# Q: Де в GitHub Pages шаблоні BodyLife дублюються акаунт, пристрій, ID сесії та завершення сеансу, і як їх консолідувати?

## Answer

Graphify query returned production Reception workflow nodes but did not surface the static docs/ui-prototype shell. Direct inspection found 13 authenticated fixtures duplicating account/device/session/logout in header-account and nav-footer. The shell was consolidated into one native details account menu in the header; opaque session ID was removed from the shell while audit-detail IDs were intentionally preserved.

## Outcome

- Signal: dead_end

## Source Nodes

- Reception
- Index.cshtml
- _ReceptionWorkspace.cshtml