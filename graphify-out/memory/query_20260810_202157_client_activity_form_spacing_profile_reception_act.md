---
type: "query"
date: "2026-08-10T20:21:57.746537+00:00"
question: "client activity form spacing profile reception action history visit layout"
contributor: "graphify"
outcome: "corrected"
correction: "Inspect docs/ui-prototype/clients.html together with docs/ui-prototype/assets/fixtures.css. The relevant structure is data-client-action-surface, not the production Reception Razor partials. Expanded vocabulary: client, activity, form, spacing, profile, reception, action, history, visit, layout."
source_nodes: ["client", "activity", "form", "spacing", "profile", "reception", "action", "history", "visit", "layout"]
---

# Q: client activity form spacing profile reception action history visit layout

## Answer

The scoped graph query surfaced production Reception files but not the static prototype stylesheet. Manual inspection found that the movable client action surface nests both the action form and activity history; direct-child profile selectors therefore missed the history, while the action workspace used a negative horizontal margin. The fix belongs in docs/ui-prototype/assets/fixtures.css: use a shared inherited profile gutter for the workspace, history header, and table; keep the table width auto inside those margins; collapse an empty live region.

## Outcome

- Signal: corrected
- Correction: Inspect docs/ui-prototype/clients.html together with docs/ui-prototype/assets/fixtures.css. The relevant structure is data-client-action-surface, not the production Reception Razor partials. Expanded vocabulary: client, activity, form, spacing, profile, reception, action, history, visit, layout.

## Source Nodes

- client
- activity
- form
- spacing
- profile
- reception
- action
- history
- visit
- layout