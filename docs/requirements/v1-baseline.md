# DeckContext V1 Requirement Baseline

Status: Baseline 0.1  
Repository: `zhou-yang-personal/deck-context`  
Product name: `DeckContext`

## 1. Problem statement

The user wants to reuse PowerPoint materials and historical deck versions as source material for ChatGPT-driven deep analysis and later PPT optimization.

A slide screenshot alone is not a sufficient or always available transport format. A plain-text extraction is also insufficient because it loses layout, object relationships, native chart/table structure, and embedded Excel data.

V1 therefore needs to convert a PowerPoint deck into a portable, text-first representation that preserves enough structural and data context for an LLM to reason about the original slides.

## 2. Confirmed V1 goal

Given a `.pptx` file, DeckContext should produce an LLM-friendly context package that represents:

1. slide text;
2. object/component layout;
3. native PowerPoint table data;
4. native PowerPoint chart data and chart structure;
5. embedded Excel workbook/chart source data when present;
6. image objects and their references, with image pixel-content understanding provided through a pluggable capability rather than assumed;
7. extraction diagnostics and provenance.

The primary use case is supplying historical PPT material to ChatGPT or another LLM for deep review, comparison, reasoning, and future slide optimization.

## 3. Primary user flow

1. User selects or drags a `.pptx` file into the Windows desktop application.
2. DeckContext parses the PPTX package and relationships.
3. DeckContext extracts supported slide objects and source data.
4. DeckContext builds a normalized intermediate representation (IR).
5. DeckContext generates human/LLM-readable Markdown and machine-readable JSON from the same IR.
6. DeckContext reports unsupported, partial, or failed extraction items explicitly.
7. User exports/opens the generated context package and supplies the relevant material to an LLM.

## 4. Required information domains

### 4.1 Slide metadata

At minimum where available:

- slide index/number;
- slide identity;
- slide size;
- slide-level ordering context.

### 4.2 Text

At minimum:

- text content;
- source object identity;
- paragraph/run structure where needed to preserve meaning;
- relevant font/size/bold/color information when it affects hierarchy or interpretation.

### 4.3 Geometry and layout

For extractable slide objects:

- object type;
- object id/name where available;
- x/y/width/height;
- normalized x/y/width/height representation where useful for LLM consumption;
- z-order/group relationship where available and materially relevant.

The output should make it possible to describe page structure and reading/layout relationships without requiring the slide screenshot as the only source.

### 4.4 Native PowerPoint tables

At minimum:

- rows and columns;
- cell values;
- merged-cell relationships where available;
- source object identity;
- important table formatting only where it affects semantic interpretation.

### 4.5 Native PowerPoint charts

At minimum where available:

- chart type;
- chart title;
- series names;
- categories;
- values;
- legend state/content;
- axis labels/units/ranges where extractable and meaningful;
- data labels where present;
- source formulas/ranges and workbook relationship when available.

Do not rely only on visible chart labels when native chart/source data is available.

### 4.6 Embedded Excel

When a chart or embedded object links to an embedded workbook, preserve/extract where available:

- workbook relationship;
- worksheet identity;
- cell/range data relevant to the chart/object;
- chart category/value formulas;
- an exported copy of the embedded workbook when useful for traceability.

### 4.7 Images

V1 must identify image objects and preserve enough information to trace them:

- slide/object identity;
- geometry;
- media/source relationship;
- extracted media file/reference where supported;
- status of image-content interpretation.

Automatic understanding of pixels inside an image is a separate capability. It must be pluggable and must not be silently fabricated when no OCR/Vision provider is configured.

## 5. Output baseline

V1 should produce at least:

### 5.1 `deck.context.md`

A text-first representation optimized for human and LLM consumption, organized by deck and slide.

It should prioritize semantic readability over raw OOXML dumping.

### 5.2 `deck.context.json`

A machine-readable representation generated from the same normalized IR.

### 5.3 `extraction-report.json`

Diagnostics that identify unsupported/partial/failed extraction at deck, slide, object, or extractor level where possible.

### 5.4 Optional extracted assets

When needed for provenance/analysis:

- embedded `.xlsx` workbooks;
- chart CSV/data extracts;
- extracted image/media files.

These are supporting artifacts; Markdown/JSON remain the primary context representation.

## 6. Delivery form baseline

V1 is a local Windows desktop application.

Reasons tied to the confirmed need:

- the source files are local PPTX documents;
- local processing reduces unnecessary cloud dependency for potentially sensitive PPT material;
- Windows is the target environment for PowerPoint-heavy workflows;
- optional PowerPoint/Office enhancement can be integrated without making Office mandatory.

V1 is not defined as a Web application or Office Add-in.

## 7. Technology baseline

- C# / .NET 10 LTS;
- WPF desktop UI;
- Open XML SDK for standard OOXML access;
- direct OOXML parsing for details not sufficiently exposed by high-level APIs;
- optional PowerPoint/Office adapter for Office-specific high-fidelity enhancements;
- pluggable OCR/Vision adapter for image pixel-content interpretation.

The core extraction path must work without:

- Microsoft PowerPoint;
- internet access;
- an OCR engine;
- a Vision API/model;
- a persistent database.

## 8. Explicit V1 non-goals

The following are **not confirmed V1 requirements** and must not be added by default:

- account/login system;
- cloud sync/storage;
- multi-user collaboration;
- remote backend service;
- vector database;
- semantic search/knowledge-base UI;
- automatic historical version diff;
- Office Add-in;
- automatic slide editing/generation;
- automatic upload into ChatGPT;
- mandatory PowerPoint installation;
- mandatory cloud OCR/Vision.

These may be evaluated later only if the user explicitly requests them.

## 9. Quality principles

### 9.1 Fidelity before summarization

Prefer source-backed extraction over inference. Preserve native data when available before generating LLM-oriented descriptions.

### 9.2 One normalized source of truth

Markdown and JSON should be projections of the same normalized IR, rather than independently parsed outputs.

### 9.3 Explicit uncertainty

Unsupported or ambiguous content must be marked as such. DeckContext must not invent missing values, chart data, image descriptions, or object relationships.

### 9.4 Partial degradation

One unsupported object should not normally invalidate the entire deck. Extract what can be extracted and report the gap.

### 9.5 Portability

The resulting Markdown/JSON should remain useful outside DeckContext and should not require a proprietary database to interpret.

## 10. Deferred decision: image pixel-content interpretation

A key unresolved V1 decision is whether image pixel-content understanding is included in the first implementation milestone and, if so, which provider strategy is used:

- local OCR;
- local multimodal model;
- external Vision API;
- configurable combination.

Until explicitly decided, the architecture must expose the capability through a provider interface but must not hard-code one provider as mandatory.

## 11. Acceptance boundary for the initial implementation

The first implementation should be considered meaningful only when a representative PPTX can demonstrate that DeckContext can:

1. parse slide text;
2. preserve object positions/layout data;
3. extract a native table;
4. extract a native chart's categories/series/values;
5. follow an embedded workbook relationship and recover relevant source data when present;
6. identify image objects without fabricating their pixel content;
7. generate deterministic Markdown and JSON from the normalized IR;
8. surface unsupported/partial extraction in diagnostics.

A successful build alone is not evidence that these requirements are met.
