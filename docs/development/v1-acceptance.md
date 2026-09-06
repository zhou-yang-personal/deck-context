# DeckContext V1 Consolidated Acceptance

Status: Automated implementation complete; manual Gates A/B/C pending

Target: the commit-matched, self-contained Windows artifact from the `dev` workflow

This checklist intentionally combines the three manual gates. One representative real presentation and one packaged application run should provide the remaining evidence without interrupting development phase by phase.

## Automated evidence

| V1 acceptance statement | Implemented evidence |
|---|---|
| 1. Parse text | Basic presentation fixture asserts paragraph/run content and formatting. |
| 2. Preserve geometry/layout | Shape and grouped-object fixtures assert native EMU geometry, effective slide-normalized geometry, hierarchical z-order paths, group child transforms, and Markdown source order. |
| 3. Parse native tables | Table fixture asserts dimensions, cell text, merges, and source identity. |
| 4. Parse native charts | Chart fixtures assert chart types, title, legend, axes, labels, and bubble-size values; unhandled graphic-frame content is explicitly unsupported. |
| 5. Preserve series/categories/values | Chart fixture asserts real cached points, ordering, formulas, and number formats. |
| 6. Follow embedded Excel | Workbook fixture asserts chart relationship, workbook part URI, relationship id, hash, and size. |
| 7. Recover workbook source data | Workbook fixture asserts worksheet metadata, A1 ranges, cells, formulas, raw values, and resolved values. |
| 8. Identify image objects | Image fixture asserts media relationship, type, filename, hash, crop, transform, and native alternative text. |
| 9. Never fabricate image semantics | Image fixtures assert the no-provider state; provider tests assert explicit local-OCR provenance, pinned-engine text recognition, image-hash-plus-crop deduplication, and scoped failure diagnostics. |
| 10. Generate Markdown and JSON from one IR | `DeckContextConversionService` reads once, then passes the same document to both deterministic serializers. |
| 11. Report unsupported objects | Unsupported chart and diagnostic-report tests assert scoped codes, severity, extractor, outcome, and provenance. |
| 12. Degrade one object without losing the deck | Malformed workbook and missing relationship tests assert partial object/deck status while retaining usable cached/native data. |
| 13. Produce genuinely readable LLM input | Markdown tests cover semantic title selection, concise extraction summaries, readable text/tables/charts, compact workbook references, formatted values, image interpretations, and condensed diagnostics; the complete trace remains in JSON. Final qualitative confirmation is manual. |

The Windows workflow downloads checksum-pinned OCR models/test data, restores, builds, runs an actual Tesseract recognition smoke test, and publishes the WPF application and verification command into one self-contained directory with a shared .NET runtime. CI checks both entry points, the local OCR native libraries, language models, app-local Visual C++ runtime files, and runtime configuration; it also confirms only one CoreCLR is packaged and the verification command starts. The complete-package tests re-run a fixture twice and compare Markdown, JSON, report, and manifest byte-for-byte, verify every manifest asset's size and SHA-256, replace an intact prior package without stale assets, and refuse to overwrite unrelated files.

The conversion session opens the PPTX once, captures immutable workbook/image asset snapshots alongside the IR, writes the full package to a sibling staging directory, and publishes it only after the manifest is complete. A failed or cancelled conversion therefore does not mix partial new output with an existing accepted package.

`manifest.json` lists the generated context/report files and extracted binary assets; it deliberately does not hash itself, avoiding a recursive self-hash.

## One-pass manual Gate A/B/C checklist

1. Download and unzip `DeckContext-dev-win-x64-{short-sha}` from the successful `dev` workflow run.
2. Run `DeckContext\DeckContext.exe`; confirm it starts without installing .NET, PowerPoint, OCR, or another dependency.
3. Drop `Ecuador_FBB_Plan_Competitive_Analysis_v4_Add_NormalPrice_CN.pptx` into the window. Also test **Browse…** once.
4. Confirm the proposed output directory is sensible; use **Choose…** to test an alternate writable folder.
5. Select **Extract context**. Confirm progress/status changes, the window remains responsive, and diagnostics are visible rather than hidden.
6. Select **Open output folder** and confirm these files exist and open: `deck.context.md`, `deck.context.json`, `extraction-report.json`, and `manifest.json`.
7. Confirm the five native charts retain chart type, series, categories, values, formulas/ranges, and chart-to-workbook linkage. Open exported `workbooks\*.xlsx` files and spot-check them against the presentation.
8. Leave **Recognize text inside images with offline OCR** enabled. Confirm screenshots with Chinese/English/Spanish text produce recognized text with `tesseract-local` provenance, nothing requests credentials or network access, repeated equal image/crop placements emit their transcription only once in Markdown, and an unreadable/unsupported image does not erase other slide content. Confirm Logo/icon/photo/map noise is omitted from Markdown with its bounded raw OCR and quality assessment retained in JSON. Disable OCR once and confirm images remain preserved with an explicit not-analyzed notice.
9. Confirm every unsupported/partial item appears in `extraction-report.json` with a useful code, severity, extractor, outcome, and source location; a local failure must not erase unaffected slides or objects.
10. Re-run the same deck into a fresh directory and compare the four primary text/JSON files. They should be byte-identical.
11. Give `deck.context.md` (and, when deeper traceability is needed, `deck.context.json`) to an LLM. Confirm it can identify slide structure, inspect table/chart data, read local-OCR transcriptions, distinguish OCR-derived text from native evidence, and use JSON when object-level geometry or provenance is needed.

## Acceptance record

Record the artifact name and commit SHA, Windows version, source deck filename/hash, pass/fail for Gate A/B/C, and any diagnostic codes requiring follow-up. Until this record is completed, V1 is implemented but not declared manually accepted.
