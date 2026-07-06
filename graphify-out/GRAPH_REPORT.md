# Graph Report - BodyLife-CRM  (2026-07-06)

## Corpus Check
- 22 files · ~22,661 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 202 nodes · 181 edges · 28 communities (24 shown, 4 thin omitted)
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `ac09d92f`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- [[_COMMUNITY_What You Must Do When Invoked|What You Must Do When Invoked]]
- [[_COMMUNITY_graphify|/graphify]]
- [[_COMMUNITY_graphify reference extra exports and benchmark|graphify reference: extra exports and benchmark]]
- [[_COMMUNITY_graphify reference query, path, explain|graphify reference: query, path, explain]]
- [[_COMMUNITY_graphify reference add a URL and watch a folder|graphify reference: add a URL and watch a folder]]
- [[_COMMUNITY_graphify reference commit hook and native CLAUDE.md integration|graphify reference: commit hook and native CLAUDE.md integration]]
- [[_COMMUNITY_graphify reference incremental update and cluster-only|graphify reference: incremental update and cluster-only]]
- [[_COMMUNITY_Step 3 - Extract entities and relationships|Step 3 - Extract entities and relationships]]
- [[_COMMUNITY_graphify reference GitHub clone and cross-repo merge|graphify reference: GitHub clone and cross-repo merge]]
- [[_COMMUNITY_graphify reference transcribe video and audio|graphify reference: transcribe video and audio]]
- [[_COMMUNITY_AGENTS|AGENTS.md]]
- [[_COMMUNITY_extraction-spec|extraction-spec.md]]
- [[_COMMUNITY_7. Бізнес-правила і формули|7. Бізнес-правила і формули]]
- [[_COMMUNITY_8. Екрани першої версії|8. Екрани першої версії]]
- [[_COMMUNITY_14. Крайні випадки, які треба врахувати|14. Крайні випадки, які треба врахувати]]
- [[_COMMUNITY_5. Основні робочі сценарії|5. Основні робочі сценарії]]
- [[_COMMUNITY_6. Дані, які треба зберігати|6. Дані, які треба зберігати]]
- [[_COMMUNITY_12. Звіти першої версії|12. Звіти першої версії]]
- [[_COMMUNITY_Data Architecture Checklist|Data Architecture Checklist]]
- [[_COMMUNITY_Observability And Operations Checklist|Observability And Operations Checklist]]
- [[_COMMUNITY_Stack Selection Matrix|Stack Selection Matrix]]
- [[_COMMUNITY_Choose Technology Stack|Choose Technology Stack]]
- [[_COMMUNITY_Interaction Decision Checklist|Interaction Decision Checklist]]
- [[_COMMUNITY_Architecture Research Protocol|Architecture Research Protocol]]
- [[_COMMUNITY_Research Architecture Options|Research Architecture Options]]
- [[_COMMUNITY_Design Data Architecture|Design Data Architecture]]
- [[_COMMUNITY_Design Observability Operations|Design Observability Operations]]
- [[_COMMUNITY_Design System Interactions|Design System Interactions]]

## God Nodes (most connected - your core abstractions)
1. `Детальна специфікація першої версії системи для залу` - 19 edges
2. `What You Must Do When Invoked` - 12 edges
3. `7. Бізнес-правила і формули` - 12 edges
4. `8. Екрани першої версії` - 12 edges
5. `5. Основні робочі сценарії` - 11 edges
6. `14. Крайні випадки, які треба врахувати` - 11 edges
7. `/graphify` - 10 edges
8. `6. Дані, які треба зберігати` - 10 edges
9. `graphify reference: extra exports and benchmark` - 8 edges
10. `Data Architecture Checklist` - 6 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Import Cycles
- None detected.

## Communities (28 total, 4 thin omitted)

### Community 0 - "What You Must Do When Invoked"
Cohesion: 0.13
Nodes (15): Part A - Structural extraction for code files, Part B - Semantic extraction (parallel subagents), Part C - Merge AST + semantic into final extraction, Step 0 - GitHub repos and multi-path merge (only if a URL or several paths), Step 1 - Ensure graphify is installed, Step 2.5 - Video and audio (only if video files detected), Step 2 - Detect files, Step 3 - Extract entities and relationships (+7 more)

### Community 1 - "/graphify"
Cohesion: 0.20
Nodes (9): For /graphify add and --watch, For /graphify query, For the commit hook and native CLAUDE.md integration, For --update and --cluster-only, /graphify, Honesty Rules, Interpreter guard for subcommands, Usage (+1 more)

### Community 2 - "graphify reference: extra exports and benchmark"
Cohesion: 0.22
Nodes (8): graphify reference: extra exports and benchmark, Step 6b - Wiki (only if --wiki flag), Step 7 - Neo4j export (only if --neo4j or --neo4j-push flag), Step 7a - FalkorDB export (only if --falkordb or --falkordb-push flag), Step 7b - SVG export (only if --svg flag), Step 7c - GraphML export (only if --graphml flag), Step 7d - MCP server (only if --mcp flag), Step 8 - Token reduction benchmark (only if total_words > 5000)

### Community 3 - "graphify reference: query, path, explain"
Cohesion: 0.33
Nodes (5): For /graphify explain, For /graphify path, graphify reference: query, path, explain, Step 0 — Constrained query expansion (REQUIRED before traversal), Step 1 — Traversal

### Community 4 - "graphify reference: add a URL and watch a folder"
Cohesion: 0.50
Nodes (3): For /graphify add, For --watch, graphify reference: add a URL and watch a folder

### Community 5 - "graphify reference: commit hook and native CLAUDE.md integration"
Cohesion: 0.50
Nodes (3): For git commit hook, For native CLAUDE.md integration, graphify reference: commit hook and native CLAUDE.md integration

### Community 6 - "graphify reference: incremental update and cluster-only"
Cohesion: 0.50
Nodes (3): For --cluster-only, For --update (incremental re-extraction), graphify reference: incremental update and cluster-only

### Community 7 - "Step 3 - Extract entities and relationships"
Cohesion: 0.11
Nodes (18): 10. Правила пошуку, 11. Разові і пробні відвідування, 13. Повідомлення і попередження всередині системи, 15. Критерії готовності першої версії, 16. Що потрібно уточнити перед або під час реалізації, 17. Погоджена логіка після останнього уточнення, 18. Підсумок, 1. Призначення документа (+10 more)

### Community 12 - "7. Бізнес-правила і формули"
Cohesion: 0.17
Nodes (12): 7.10. Денний звіт, 7.11. Попередження про завершення абонемента, 7.1. Основна формула дати завершення абонемента, 7.2. Формула залишку занять, 7.3. Мінусові заняття, 7.4. Дата старту нового абонемента при мінусових заняттях, 7.5. Активність абонемента, 7.6. Заморозка (+4 more)

### Community 13 - "8. Екрани першої версії"
Cohesion: 0.17
Nodes (12): 8.10. Клієнти, які давно не ходили, 8.11. Налаштування типів абонементів, 8.1. Вхід у систему, 8.2. Головний екран рецепції, 8.3. Профіль клієнта, 8.4. Екран видачі абонемента, 8.5. Екран візитів, 8.6. Екран оплат (+4 more)

### Community 14 - "14. Крайні випадки, які треба врахувати"
Cohesion: 0.18
Nodes (11): 14.10. Перетин заморозки і неробочих днів, 14.1. Клієнт без картки, 14.2. Дублікати клієнтів, 14.3. Дублікати номерів карток, 14.4. Візит без активного абонемента, 14.5. Абонемент закінчився по даті, але заняття залишились, 14.6. Абонемент активний по даті, але заняття закінчились, 14.7. Скасування візиту, який створив мінус (+3 more)

### Community 15 - "5. Основні робочі сценарії"
Cohesion: 0.18
Nodes (11): 5.10. Клієнти, які давно не ходили, 5.1. Клієнт прийшов у зал з карткою, 5.2. Клієнт прийшов без картки, 5.3. Новий клієнт, 5.4. Видача абонемента, 5.5. Відмітка візиту, 5.6. Скасування помилкового візиту, 5.7. Оплата (+3 more)

### Community 16 - "6. Дані, які треба зберігати"
Cohesion: 0.20
Nodes (10): 6.1. Клієнт, 6.2. Номер картки, 6.3. Тип абонемента, 6.4. Абонемент клієнта, 6.5. Візит, 6.6. Оплата, 6.7. Заморозка, 6.8. Неробочий день або період (+2 more)

### Community 17 - "12. Звіти першої версії"
Cohesion: 0.33
Nodes (6): 12.1. Денний звіт, 12.2. Абонементи, які скоро закінчуються, 12.3. Абонементи з малим залишком занять, 12.4. Мінусові клієнти, 12.5. Клієнти, які давно не ходили, 12. Звіти першої версії

### Community 18 - "Data Architecture Checklist"
Cohesion: 0.29
Nodes (6): Data Architecture Checklist, Domain Entities To Check, Invariants, Output Template, Scenario Validation, Storage Decisions

### Community 19 - "Observability And Operations Checklist"
Cohesion: 0.29
Nodes (6): Backup And Restore, Business Audit History, Metrics, Observability And Operations Checklist, Output Template, Technical Logs

### Community 20 - "Stack Selection Matrix"
Cohesion: 0.33
Nodes (5): Criteria, Decision Record, Output Matrix, Required Inputs, Stack Selection Matrix

### Community 21 - "Choose Technology Stack"
Cohesion: 0.33
Nodes (5): Choose Technology Stack, Compare These Areas, Guardrails, Overview, Workflow

### Community 22 - "Interaction Decision Checklist"
Cohesion: 0.33
Nodes (5): Boundary Questions, Flows To Model, Interaction Decision Checklist, Output Template, Patterns To Compare

### Community 23 - "Architecture Research Protocol"
Cohesion: 0.33
Nodes (5): Architecture Research Protocol, Decision Areas, Evidence Order, Output Template, Questions To Answer

### Community 24 - "Research Architecture Options"
Cohesion: 0.33
Nodes (5): BodyLife Anchors, Guardrails, Overview, Research Architecture Options, Workflow

### Community 25 - "Design Data Architecture"
Cohesion: 0.40
Nodes (4): Design Data Architecture, Guardrails, Overview, Workflow

### Community 26 - "Design Observability Operations"
Cohesion: 0.40
Nodes (4): Design Observability Operations, Guardrails, Overview, Workflow

### Community 27 - "Design System Interactions"
Cohesion: 0.40
Nodes (4): Design System Interactions, Guardrails, Overview, Workflow

## Knowledge Gaps
- **152 isolated node(s):** `Overview`, `Workflow`, `Compare These Areas`, `Guardrails`, `Required Inputs` (+147 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **4 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Детальна специфікація першої версії системи для залу` connect `Step 3 - Extract entities and relationships` to `7. Бізнес-правила і формули`, `8. Екрани першої версії`, `14. Крайні випадки, які треба врахувати`, `5. Основні робочі сценарії`, `6. Дані, які треба зберігати`, `12. Звіти першої версії`?**
  _High betweenness centrality (0.142) - this node is a cross-community bridge._
- **Why does `7. Бізнес-правила і формули` connect `7. Бізнес-правила і формули` to `Step 3 - Extract entities and relationships`?**
  _High betweenness centrality (0.040) - this node is a cross-community bridge._
- **Why does `8. Екрани першої версії` connect `8. Екрани першої версії` to `Step 3 - Extract entities and relationships`?**
  _High betweenness centrality (0.040) - this node is a cross-community bridge._
- **What connects `Overview`, `Workflow`, `Compare These Areas` to the rest of the system?**
  _152 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `What You Must Do When Invoked` be split into smaller, more focused modules?**
  _Cohesion score 0.13333333333333333 - nodes in this community are weakly interconnected._
- **Should `Step 3 - Extract entities and relationships` be split into smaller, more focused modules?**
  _Cohesion score 0.10526315789473684 - nodes in this community are weakly interconnected._