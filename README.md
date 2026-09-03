# DeckContext

DeckContext converts PowerPoint decks into structured, LLM-friendly context while preserving the information that matters for deep analysis: text, layout, native tables, charts, embedded Excel data, and image references/semantics.

## Repository

- GitHub repository: `deck-context`
- Product / application name: `DeckContext`
- Primary platform for V1: Windows desktop
- Primary development branch: `dev`
- Stable baseline branch: `main`

## V1 Goal

Convert a `.pptx` file into a portable AI context package that can be supplied to ChatGPT or other LLMs without relying on slide screenshots as the primary representation.

The V1 baseline focuses on:

- slide and object text;
- object geometry and reading/layout information;
- native PowerPoint tables;
- native PowerPoint charts;
- embedded Excel workbooks and chart source data;
- image extraction/reference plus a pluggable image-to-text capability;
- LLM-friendly Markdown output;
- machine-readable JSON output;
- extraction diagnostics and traceability.

## Architecture Direction

V1 is a local Windows desktop application based on:

- C# / .NET 10 LTS;
- WPF for the desktop UI;
- Open XML SDK for PPTX / OOXML parsing;
- direct OOXML parsing where higher-fidelity access is required;
- an optional PowerPoint/Office adapter for high-fidelity rendering or Office-specific enhancement;
- a pluggable image text/vision adapter rather than a mandatory cloud dependency.

The core extraction pipeline must remain usable without Microsoft PowerPoint, OCR, a vision model, a database, or a cloud service.

## Documentation

- [`AGENTS.md`](AGENTS.md) — repository execution rules and development constraints.
- [`docs/requirements/v1-baseline.md`](docs/requirements/v1-baseline.md) — confirmed V1 requirements and explicit non-goals.
- [`docs/architecture/5-view-architecture-v0.1.md`](docs/architecture/5-view-architecture-v0.1.md) — V1 architecture using the 4+1 / five-view model.
- [`docs/development/v1-execution-backlog.md`](docs/development/v1-execution-backlog.md) — Phase 0–9 implementation backlog, acceptance evidence, risks, and manual gates.
- [`docs/development/work-bootstrap-prompt.md`](docs/development/work-bootstrap-prompt.md) — complete Work development and delivery protocol.

## Build and test

The repository requires the .NET 10 SDK. From the repository root:

```powershell
dotnet restore DeckContext.sln
dotnet build DeckContext.sln --configuration Release --no-restore
dotnet test DeckContext.sln --configuration Release --no-build
```

Pushes to `dev` and manual workflow dispatches run the same checks on Windows, publish the WPF shell and verification command as self-contained `win-x64` packages, and upload a commit-traceable GitHub Actions artifact.

For the Phase 5 chart/workbook verification package, run:

```powershell
.\DeckContext.Verification\DeckContext.Verification.exe "C:\path\input.pptx" "C:\path\deck-context-output"
```

The command writes `deck.context.json`, exports traceable embedded workbooks under `workbooks\`, and exports internal image media under `images\`. Exported binary assets are checked against the SHA-256 stored in the context. It is a development verification surface; the end-user conversion workflow remains scheduled for Phase 8.

## Branch Strategy

- `main`: baselined, reviewable project state.
- `dev`: primary integration and day-to-day development branch.

Unless explicitly requested otherwise, future implementation work should target `dev`, not `main`.

## Status

Phases 0–4 are complete on `dev`; Phase 5 implementation is complete and remains pending Gate A acceptance against the supplied real-world deck. Phase 6 identifies image objects, preserves media relationships, native alternative text, crop/transform data and hashes, exports exact image assets, and explicitly reports `NotConfigured` when no OCR/Vision provider is selected. The current WPF shell intentionally exposes no extraction workflow before Phase 8.
