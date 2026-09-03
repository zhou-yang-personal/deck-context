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

Pushes to `dev` and manual workflow dispatches run the same checks on Windows, publish the WPF application and verification command as self-contained `win-x64` packages, and upload a commit-traceable GitHub Actions artifact.

## Use the Windows application

Download and unzip the latest `DeckContext-dev-win-x64-{short-sha}` artifact, then run:

```powershell
.\DeckContext\DeckContext.exe
```

Select or drop a `.pptx`, optionally choose an output folder, then select **Extract context**. The generated package contains:

- `deck.context.md` — readable deck/slide/object context for humans and LLMs;
- `deck.context.json` — the complete normalized intermediate representation;
- `extraction-report.json` — explicit information, warning, error, skipped, partial, and recovered diagnostics;
- `manifest.json` — hashes, sizes, provenance, and relative paths for generated and extracted assets;
- `workbooks\` — exact embedded workbook assets when present;
- `images\` — exact internal image media when present.

The same pipeline is available as a command for repeatable verification or automation:

```powershell
.\DeckContext.Verification\DeckContext.Verification.exe "C:\path\input.pptx" "C:\path\deck-context-output"
```

Both entry points read the PPTX once into the same IR, then project Markdown, JSON, diagnostics, manifest, and assets from that result. Images are preserved and identified, but pixel semantics are explicitly reported as not analyzed until an OCR/Vision provider is configured.

## Branch Strategy

- `main`: baselined, reviewable project state.
- `dev`: primary integration and day-to-day development branch.

Unless explicitly requested otherwise, future implementation work should target `dev`, not `main`.

## Status

V1 Phases 0–9 are implemented on `dev`. Automated fixture, determinism, partial-degradation, architecture, exporter, pipeline, and view-model evidence runs in Windows CI. Final acceptance remains intentionally open until the packaged application and a representative real deck pass the consolidated Gate A/B/C manual checklist in [`docs/development/v1-acceptance.md`](docs/development/v1-acceptance.md).
