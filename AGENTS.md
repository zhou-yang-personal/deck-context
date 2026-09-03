# AGENTS.md

This file is the repository-level execution contract for `zhou-yang-personal/deck-context`.

## 1. Mandatory first step

Before any requirement design, architecture change, code modification, UI adjustment, refactor, release preparation, or PR review:

1. Read this `AGENTS.md` from the repository root.
2. Inspect the current branch and relevant existing files.
3. Preserve the confirmed V1 scope unless the user explicitly changes it.
4. Do not infer or add product requirements merely because they are common in similar tools.

If this file cannot be read, stop implementation/design work and ask the user to provide its contents.

## 2. Branch workflow

- `main` is the stable/baselined branch.
- `dev` is the primary integration and day-to-day development branch.
- Unless the user explicitly requests otherwise, implementation changes must target `dev`.
- Feature branches, if later introduced, should branch from `dev` and merge back to `dev`.
- Do not commit implementation work directly to `main` unless the user explicitly asks for a baseline/release update.

## 3. Product boundary

DeckContext converts PowerPoint `.pptx` files into structured, LLM-friendly context for downstream analysis and PPT optimization.

Confirmed V1 information domains:

- slide text and text formatting relevant to interpretation;
- object type and geometry/layout;
- native PowerPoint tables;
- native PowerPoint charts and chart metadata;
- embedded Excel workbooks / chart source data;
- image extraction/reference and a pluggable image-to-text/vision interface;
- LLM-friendly Markdown output;
- machine-readable JSON output;
- extraction diagnostics and traceability.

V1 is **not** automatically expanded into a knowledge-base product. Do not add the following unless explicitly requested:

- user accounts or authentication;
- cloud sync;
- multi-user collaboration;
- hosted backend services;
- vector database / semantic search;
- project/workspace management;
- Office Add-in integration;
- automatic PPT editing/generation;
- version-diff engine;
- mandatory external Vision/OCR API dependency.

## 4. Architecture baseline

Current V1 direction:

- Platform: Windows desktop.
- Language/runtime: C# / .NET 10 LTS.
- UI: WPF.
- PPTX/OOXML: Open XML SDK plus direct OOXML parsing where needed.
- Embedded Excel: parse OOXML workbooks directly; retain source references when possible.
- PowerPoint/Office integration: optional adapter only, not a core extraction dependency.
- OCR/Vision: pluggable adapter; core extraction must work without it.
- Persistent database: not required for V1.

Core extraction must remain usable when Microsoft PowerPoint, OCR/Vision providers, and network connectivity are unavailable.

## 5. Core pipeline constraint

Keep the core flow conceptually stable unless evidence requires a change:

`PPTX -> Package/Relationship Reader -> Slide/Object Extractors -> Normalized Intermediate Representation -> Markdown/JSON Export -> Extraction Report`

Optional capabilities (rendering, OCR, Vision) must attach through explicit interfaces/adapters and must not contaminate the core OOXML domain model.

## 6. Data-model principles

The intermediate representation (IR) is the architectural center of the application.

Requirements for the IR:

- preserve source slide/object identity where available;
- preserve source relationships and provenance for extracted data;
- use both native geometry and normalized geometry where useful;
- distinguish native tables, native charts, images, text boxes, groups, and other shapes;
- preserve chart series/categories/values/source formulas when available;
- preserve embedded workbook linkage when available;
- represent extraction uncertainty/failure explicitly rather than silently inventing values;
- support deterministic Markdown and JSON generation from the same normalized model.

## 7. Parsing discipline

- Do not use slide screenshots as the primary source for text/table/chart data when native OOXML is available.
- Do not infer hidden chart data from visible labels when embedded/source data can be extracted.
- Do not flatten native tables/charts into untraceable prose before the normalized IR is built.
- Do not silently repair ambiguous or malformed source data; record diagnostics.
- Preserve units, labels, categories, series names, formulas/ranges, and source references where present.

## 8. Image handling

V1 must at minimum be able to identify and extract/reference images as PPT objects.

Image pixel-content interpretation is intentionally separated behind an interface such as `IImageTextProvider` or equivalent.

Until the user selects a concrete provider strategy:

- do not make cloud Vision mandatory;
- do not assume local OCR is installed;
- do not invent image descriptions when no provider has analyzed the image;
- record the image object, location, source/media reference, and extraction status.

## 9. UI principles for V1

Do not turn V1 into a large document-management UI.

The UI should serve the confirmed conversion workflow:

1. select/drop a PPTX;
2. inspect extraction progress/status;
3. see important warnings/errors;
4. choose/export the context package;
5. open the generated output location.

Any additional screens or workflow concepts require an explicit user need.

## 10. Error handling and observability

Extraction should degrade per object/slide where possible rather than failing the entire deck for one unsupported object.

Diagnostics should identify at least:

- source file;
- slide number/id;
- object id/name when available;
- extractor/component;
- severity;
- message;
- whether data was skipped, partially extracted, or recovered.

Logs and generated reports must not hide extraction gaps.

## 11. Testing expectations

For implementation work, prefer fixture-driven tests with small PPTX samples covering the relevant object types.

At minimum, tests should verify the changed behavior for:

- text extraction;
- coordinates/layout normalization;
- tables;
- charts;
- embedded Excel relationships/data;
- unsupported/malformed object handling;
- Markdown/JSON deterministic output where affected.

Do not claim support based only on successful compilation.

## 12. Versioning and documentation

When a task changes a version number, update all relevant version markers consistently across application, configuration, packaging, and documentation.

When architecture or confirmed product scope changes, update the corresponding baseline documentation in the same change.

## 13. Delivery requirements

For code modifications requested in chat:

- preserve the repository's original directory structure;
- provide a downloadable ZIP containing only files changed in that delivery, with their repository-relative paths preserved;
- summarize modification goal, changed-file list, core logic, usage, and verification method;
- provide the download link;
- do not include unrelated generated files.

Long design/architecture documents should preferably live in repository documentation rather than being dumped only into chat.

## 14. CI build and manual verification gate

When a phase or change requires the user to verify behavior on a real Windows machine, the verification package must be produced automatically by GitHub Actions. Do not require the user to clone the repository, install the .NET SDK, open Visual Studio, or build/publish locally.

### 14.1 Development workflow requirement

Phase 0 must establish a GitHub Actions development build workflow, normally under `.github/workflows/dev-build.yml`, that supports at least:

- pushes to `dev`;
- manual `workflow_dispatch`;
- restore;
- Release build;
- automated tests;
- Windows `win-x64` publish;
- packaging of the runnable output;
- upload of the package as a GitHub Actions artifact.

The development verification package should be a self-contained Windows package unless implementation evidence requires a different packaging choice. Do not introduce installer/MSIX/code-signing work unless explicitly requested.

### 14.2 Artifact traceability

Every manual-verification artifact must be traceable to the exact source version. The phase report must identify at least:

- branch;
- full commit SHA;
- short commit SHA;
- build configuration;
- runtime target;
- workflow run;
- automated test result;
- artifact name;
- direct artifact download link when available.

Recommended artifact naming:

`DeckContext-dev-win-x64-{short-sha}`

Never present an old artifact as the result of a newer code change.

### 14.3 CI gate

A build may be handed to the user for manual verification only after all applicable steps succeed:

- restore;
- Release build;
- automated tests;
- publish;
- artifact upload.

If any required step fails, report `CI Failed`, fix the problem, and rebuild before requesting manual verification.

### 14.4 Manual verification decision

Every phase report must contain:

`Manual Verification Required: Yes / No`

Prefer automated verification when behavior can be reliably asserted by tests. Manual verification is particularly appropriate for:

- WPF UI and drag/drop/file dialogs;
- Windows file-system behavior and output-folder workflow;
- optional PowerPoint Interop behavior;
- complex real-world PPT chart extraction;
- embedded workbook extraction against real-world PPTs;
- whether generated Markdown is genuinely usable as LLM material;
- packaged application startup on the target Windows environment.

At minimum, plan explicit manual verification gates around:

- native Chart + Embedded Excel integration;
- WPF UI completion;
- final V1 acceptance.

### 14.5 Manual verification handoff

When `Manual Verification Required: Yes`, the report must provide:

- source commit;
- CI status;
- workflow run;
- artifact name;
- artifact download link;
- minimal run instructions;
- a short checklist limited to behavior changed in that phase;
- expected result;
- known limitations.

Do not ask the user to perform development commands.

If manual verification is a blocking gate, do not mark that phase `Accepted` until the user reports the result. If the user finds a defect, fix it, rerun automated tests/CI, generate a new artifact, and provide the new artifact rather than reusing the old package.

### 14.6 Artifact vs release

Use GitHub Actions artifacts for normal `dev` verification builds.

Do not create a GitHub Release for every development build. Releases/assets are reserved for an explicitly requested baseline/release/main promotion.

## 15. Decision hierarchy

When requirements conflict, apply in this order:

1. user's explicit instruction in the current task;
2. this `AGENTS.md`;
3. confirmed V1 baseline documents;
4. existing architecture/code conventions;
5. general engineering practice.

General engineering practice must never be used to invent product scope.
