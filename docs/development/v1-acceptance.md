# DeckContext V1 Consolidated Acceptance

Status: Automated implementation complete; manual Gates A/B/C pending

Target: the commit-matched, self-contained Windows artifact from the `dev` workflow

This checklist intentionally combines the three manual gates. One representative real presentation and one packaged application run should provide the remaining evidence without interrupting development phase by phase.

## Automated evidence

| V1 acceptance statement | Implemented evidence |
|---|---|
| 1. Parse text | Basic presentation fixture asserts paragraph/run content and formatting. |
| 2. Preserve geometry/layout | Shape and grouped-object fixtures assert native EMU geometry, normalized geometry, z-order, and group provenance. |
| 3. Parse native tables | Table fixture asserts dimensions, cell text, merges, and source identity. |
| 4. Parse native charts | Chart fixture asserts chart types, title, legend, axes, and labels. |
| 5. Preserve series/categories/values | Chart fixture asserts real cached points, ordering, formulas, and number formats. |
| 6. Follow embedded Excel | Workbook fixture asserts chart relationship, workbook part URI, relationship id, hash, and size. |
| 7. Recover workbook source data | Workbook fixture asserts worksheet metadata, A1 ranges, cells, formulas, raw values, and resolved values. |
| 8. Identify image objects | Image fixture asserts media relationship, type, filename, hash, crop, transform, and native alternative text. |
| 9. Never fabricate image semantics | Image fixture and Markdown tests assert `NotConfigured` and “Pixel content: not analyzed.” |
| 10. Generate Markdown and JSON from one IR | `DeckContextConversionService` reads once, then passes the same document to both deterministic serializers. |
| 11. Report unsupported objects | Unsupported chart and diagnostic-report tests assert scoped codes, severity, extractor, outcome, and provenance. |
| 12. Degrade one object without losing the deck | Malformed workbook and missing relationship tests assert partial object/deck status while retaining usable cached/native data. |
| 13. Produce genuinely readable LLM input | Markdown structure tests cover deck summary, slides, source-ordered objects, text, tables, charts, workbook cells, images, and diagnostics; final qualitative confirmation is manual. |

The Windows workflow restores, builds, tests, and publishes both the WPF application and verification command. The complete-package tests also re-run a fixture twice and compare Markdown, JSON, report, and manifest byte-for-byte, then verify every manifest asset's size and SHA-256.

## One-pass manual Gate A/B/C checklist

1. Download and unzip `DeckContext-dev-win-x64-{short-sha}` from the successful `dev` workflow run.
2. Run `DeckContext\DeckContext.exe`; confirm it starts without installing .NET, PowerPoint, OCR, or another dependency.
3. Drop `Ecuador_FBB_Plan_Competitive_Analysis_v4_Add_NormalPrice_CN.pptx` into the window. Also test **Browse…** once.
4. Confirm the proposed output directory is sensible; use **Choose…** to test an alternate writable folder.
5. Select **Extract context**. Confirm progress/status changes, the window remains responsive, and diagnostics are visible rather than hidden.
6. Select **Open output folder** and confirm these files exist and open: `deck.context.md`, `deck.context.json`, `extraction-report.json`, and `manifest.json`.
7. Confirm the five native charts retain chart type, series, categories, values, formulas/ranges, and chart-to-workbook linkage. Open exported `workbooks\*.xlsx` files and spot-check them against the presentation.
8. For any image object, confirm the media relationship and exported image are traceable and the output does not invent pixel meaning. Without a provider it must state that pixel content was not analyzed.
9. Confirm every unsupported/partial item appears in `extraction-report.json` with a useful code, severity, extractor, outcome, and source location; a local failure must not erase unaffected slides or objects.
10. Re-run the same deck into a fresh directory and compare the four primary text/JSON files. They should be byte-identical.
11. Give `deck.context.md` (and, when deeper traceability is needed, `deck.context.json`) to ChatGPT. Confirm it can identify slide structure, cite source slide/object facts, inspect table/chart data, and distinguish extracted evidence from unavailable image semantics.

## Acceptance record

Record the artifact name and commit SHA, Windows version, source deck filename/hash, pass/fail for Gate A/B/C, and any diagnostic codes requiring follow-up. Until this record is completed, V1 is implemented but not declared manually accepted.
