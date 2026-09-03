# DeckContext V1 Execution Backlog

Status: Active  
Target branch: `dev`  
Source of truth: `AGENTS.md`, `docs/requirements/v1-baseline.md`, and `docs/architecture/5-view-architecture-v0.1.md`

## Planning rules

- The backlog decomposes the confirmed V1 baseline; it does not add product scope.
- Work proceeds phase by phase. A later phase may refine an implementation detail, but must not silently change a confirmed requirement or architecture boundary.
- Every phase must complete inspect, plan, implement, test, review, commit, CI, and report activities.
- Small, deterministic PPTX fixtures are the primary automated evidence. Real-world decks supplement them at explicit manual gates.
- A phase is not accepted merely because it compiles.

## Phase backlog

| Phase / milestone | Task | Dependency | Acceptance criteria | Test fixture / evidence | Primary risk | Manual verification required |
|---|---|---|---|---|---|---|
| Phase 0 — Engineering Bootstrap | Create the .NET solution, WPF shell, Domain/Application/OpenXml/Export projects, central build/package configuration, architecture tests, and the `dev` CI workflow. | Baseline documents | Release restore/build/test succeed; dependency direction is asserted; `win-x64` self-contained publish is uploaded as a commit-traceable artifact. | Architecture tests and successful GitHub Actions run | Linux work environment cannot compile WPF locally; Windows CI is authoritative. | No — no user-facing extraction behavior is accepted in this phase. |
| Phase 1 — Core IR + Package/Slide Foundation | Define the smallest evolvable deck/slide/element identity, source reference, geometry, status, and diagnostic contracts. Open a PPTX, enumerate presentation/slides, resolve slide relationships, and serialize the initial IR. | Phase 0 | Correct slide count/order/size/identity; package and relationship failures are explicit; initial JSON is deterministic. | `presentation-basic.pptx`, malformed package fixture, JSON golden file | Prematurely over-modeling later table/chart/image concerns. | No, if fixture tests and CI fully prove the behavior. |
| Phase 2 — Text + Geometry/Layout | Extract shape text with paragraph/run structure and materially relevant formatting. Extract native and normalized geometry, z-order, and basic group relationships. | Phase 1 | Exact text and run hierarchy are source-backed; native/normalized geometry are separate; group provenance is retained; failures are diagnosed. | `text-only.pptx`, `layout-basic.pptx`, `groups-basic.pptx` | Theme inheritance and nested transforms can create false formatting or coordinates. | No, unless real-file behavior cannot be proven by fixtures. |
| Phase 3 — Native PowerPoint Tables | Map native table identity, rows, columns, cells, merges, geometry, and semantic formatting into structured IR. | Phase 2 | Row/column counts, cell values, merged-cell relations, object identity, and deterministic outputs are asserted. | `table-basic.pptx` plus expected IR/Markdown | Merge semantics and inherited table styles may be misrepresented. | No, if fixture coverage is complete. |
| Phase 4 — Native PowerPoint Charts | Extract chart identity/type/title, series, categories, values, legend, axes, labels, units/ranges, formulas, relationships, and chart diagnostics. | Phase 2; Phase 3 is not a data dependency but should be complete first | Tests assert real categories/series/values and formulas, not only object creation; unsupported chart variants degrade per object. | `chart-basic.pptx`, unsupported chart variant fixture | Chart XML variants, caches, formulas, and missing/partial data can disagree. | Deferred to the combined Chart + Embedded Excel gate after Phase 5. |
| Phase 5 — Embedded Excel | Follow chart relationships to embedded workbooks; map worksheet/range/cell data; preserve formula and value provenance; export workbook assets when useful. | Phase 4 | Chart-to-workbook-to-sheet/range chain is traceable; category/value cells match chart mapping; partial workbook problems mark the chart Partial. | `chart-embedded-workbook.pptx` plus one real-world chart deck | Relationship and formula resolution can be valid syntactically but map the wrong cells. | Yes — Gate A, using a real PPTX and downloadable Windows artifact. |
| Phase 6 — Images/Media | Identify image elements, resolve/extract media, preserve geometry and relevant crop/transform data, and define the image-text provider boundary with a NotConfigured result. | Phase 2 | Images are traceable to media assets; output explicitly states image content was not analyzed without a provider; no image semantics are fabricated. | `images-basic.pptx` | Accidentally treating image OCR as a core dependency or losing crop/transform provenance. | No, unless target-environment media export differs from fixtures. |
| Phase 7 — Markdown/JSON/Diagnostics | Generate stable LLM-friendly Markdown, complete machine-readable JSON, and an extraction report from the same IR; export supporting assets through a manifest. | Phases 1–6 | Deterministic outputs; Markdown preserves slide/object/data structure without becoming an XML dump; report exposes skipped/partial/recovered items. | Golden Markdown/JSON/report files for all fixtures | Readability improvements may accidentally hide source facts or diverge from JSON. | Yes only for qualitative LLM-readability review if automated golden tests are insufficient. |
| Phase 8 — Minimal WPF Conversion UI | Implement select/drop PPTX, start extraction, progress/status, warnings/errors, export result, and open output location. | Phase 7 | UI remains responsive; each confirmed workflow action works on packaged Windows app; no library/workspace/dashboard scope is added. | View-model tests plus packaged application | Windows drag/drop, dialogs, background execution, and file access need target-environment validation. | Yes — Gate B. |
| Phase 9 — Integration and V1 Acceptance | Run all fixtures and at least one representative real deck end to end; validate fidelity, provenance, partial degradation, deterministic outputs, package startup, and LLM usefulness. | Phases 1–8 and accepted Gates A/B | All 13 V1 acceptance statements in the development bootstrap are evidenced; every known unsupported object is reported; no P0 extraction gap is hidden. | Full fixture suite, Ecuador reference deck or another approved real deck, final Windows artifact | Real presentations contain OOXML combinations not represented by small fixtures. | Yes — Gate C. |

## Explicit manual gates

### Gate A — Native Chart and Embedded Excel

Evidence must include a successful Windows CI artifact tied to the exact commit, automatic fixture assertions, and a real PPT review of series, categories, values, formulas/ranges, workbook linkage, Markdown representation, and absence of unsupported inference.

### Gate B — WPF workflow

Evidence must cover application startup, select/drop, conversion progress, diagnostics display, export folder behavior, and opening generated output on the target Windows environment.

### Gate C — V1 acceptance

Evidence must cover the complete package, representative real content, stable degradation, all primary outputs, traceability, and actual usefulness as ChatGPT/LLM source material.

## Deferred decision register

| Decision | Current status | Boundary until decided |
|---|---|---|
| Image pixel-content provider | Deferred | Define an interface and explicit NotConfigured/NotAnalyzed behavior only. Do not select or integrate OCR/Vision. |
| PowerPoint Interop enhancement | Optional | Core extraction and CI acceptance must not require Microsoft PowerPoint. |
| Installer/MSIX/code signing | Out of Phase 0 and not requested | Use a self-contained `win-x64` GitHub Actions artifact for development verification. |
