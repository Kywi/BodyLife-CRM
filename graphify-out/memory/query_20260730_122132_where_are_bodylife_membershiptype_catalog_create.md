---
type: "query"
date: "2026-07-30T12:21:32.455409+00:00"
question: "Where are BodyLife MembershipType catalog create, update, deactivate handlers, immutable issued snapshots, and Owner GitHub Pages fixture contracts?"
contributor: "graphify"
outcome: "dead_end"
source_nodes: ["Owner", "Membership", "MembershipType"]
---

# Q: Where are BodyLife MembershipType catalog create, update, deactivate handlers, immutable issued snapshots, and Owner GitHub Pages fixture contracts?

## Answer

Expanded from original query via vocab: [membership, catalog, create, update, deactivate, owner, snapshot, duration, visits, price, handler, lifecycle]. BFS depth 2 returned only broad Owner and Membership nodes from initial context plus localization; it did not surface the actual Razor handlers, ADR-011, commands, tests, or prototype file, so direct source inspection was required.

## Outcome

- Signal: dead_end

## Source Nodes

- Owner
- Membership
- MembershipType