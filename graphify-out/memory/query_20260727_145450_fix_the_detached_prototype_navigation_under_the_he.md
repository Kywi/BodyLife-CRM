---
type: "query"
date: "2026-07-27T14:54:50.787524+00:00"
question: "Fix the detached prototype navigation under the header on scroll and make History/Audit rows fully rounded like Home activity cards"
contributor: "graphify"
outcome: "dead_end"
source_nodes: ["Audit"]
---

# Q: Fix the detached prototype navigation under the header on scroll and make History/Audit rows fully rounded like Home activity cards

## Answer

Expanded from original request via graph vocab: [header, navigation, audit, history, timeline, card, layout, shell, home, prototype]. BFS reached domain and localization Audit nodes, not docs/ui-prototype CSS or DOM. This traversal is a dead end for the visual defect; direct styles.css, fixtures.css, app.js and Chromium geometry are authoritative.

## Outcome

- Signal: dead_end

## Source Nodes

- Audit