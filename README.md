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

The current machine-readable context schema is `0.4`; it separates line-filtered Markdown OCR from bounded raw OCR evidence in JSON. Schema `0.3` added OCR quality assessment and Markdown publication decisions, while `0.2` added hierarchical group ordering/transforms and bubble-chart size data to the original `0.1` contract.

## Architecture Direction

V1 is a local Windows desktop application based on:

- C# / .NET 10 LTS;
- WPF for the desktop UI;
- Open XML SDK for PPTX / OOXML parsing;
- direct OOXML parsing where higher-fidelity access is required;
- an optional PowerPoint/Office adapter for high-fidelity rendering or Office-specific enhancement;
- a pluggable image text adapter with a bundled offline Tesseract implementation.

The core extraction pipeline remains usable without Microsoft PowerPoint, OCR, a database, or a cloud service. The packaged desktop app adds local CPU OCR without uploading source data.

## Documentation

- [`AGENTS.md`](AGENTS.md) — repository execution rules and development constraints.
- [`docs/requirements/v1-baseline.md`](docs/requirements/v1-baseline.md) — confirmed V1 requirements and explicit non-goals.
- [`docs/architecture/5-view-architecture-v0.1.md`](docs/architecture/5-view-architecture-v0.1.md) — V1 architecture using the 4+1 / five-view model.
- [`docs/development/v1-execution-backlog.md`](docs/development/v1-execution-backlog.md) — Phase 0–9 implementation backlog, acceptance evidence, risks, and manual gates.
- [`docs/development/work-bootstrap-prompt.md`](docs/development/work-bootstrap-prompt.md) — complete Work development and delivery protocol.

## Build and test

The repository requires the .NET 10 SDK. From the repository root:

```powershell
./scripts/Prepare-OcrAssets.ps1 -DestinationDirectory artifacts/ocr/tessdata -TestFixtureDirectory artifacts/ocr-test
$env:DECKCONTEXT_TEST_OCR_DATA = "$PWD\artifacts\ocr\tessdata"
$env:DECKCONTEXT_TEST_OCR_IMAGE = "$PWD\artifacts\ocr-test\phototest.tif"
dotnet restore DeckContext.sln --runtime win-x64
dotnet build DeckContext.sln --configuration Release --no-restore
dotnet test DeckContext.sln --configuration Release --no-build
```

Pushes to `dev` and manual workflow dispatches run the same checks on Windows, publish the WPF application and verification command as one self-contained `win-x64` package with a shared .NET runtime, and upload a commit-traceable GitHub Actions artifact.

## Use the Windows application

Download and unzip the latest `DeckContext-dev-win-x64-{short-sha}` artifact, then run:

```powershell
.\DeckContext\DeckContext.exe
```

Extract the complete artifact before launching; the executable depends on the DLLs beside it. If managed application startup fails, DeckContext shows the error and writes details to `%LOCALAPPDATA%\DeckContext\Logs\application-errors.log`.

The desktop workflow accepts one or more PowerPoint files. Batch inputs are processed sequentially and each deck is published into its own `{file-name}.deck-context` folder under the selected output root. One failed deck is reported without stopping the remaining queue. The verification command continues to process one input file per invocation.

Select or drop one or more `.pptx` files, optionally choose an output folder, then select **Extract context**. A single selection keeps the original direct-package output behavior; a multiple selection uses the chosen folder as the batch output root. Each generated package contains:

- `deck.context.md` — readable deck/slide/object context for humans and LLMs;
- `deck.context.json` — the complete normalized intermediate representation;
- `extraction-report.json` — explicit information, warning, error, skipped, partial, and recovered diagnostics;
- `manifest.json` — hashes, sizes, provenance, and relative paths for generated and extracted assets;
- `workbooks\` — exact embedded workbook assets when present;
- `images\` — exact internal image media when present.

Image OCR is enabled by default and remains fully local. The package includes Tesseract 5 plus `tessdata_fast` models for Simplified Chinese, English, and Spanish; no account, API key, network access, PowerPoint, or separate OCR installation is required. Disable **Recognize text inside images with offline OCR** when only native PPTX structure and extracted assets are needed. OCR transcribes visible text; it does not invent a semantic description of photos, maps, or diagrams. Confidence, line structure, and character-noise filters keep likely Logo, icon, photo, and map fragments out of Markdown; low-quality and empty results are summarized once rather than repeated per image. Bounded raw OCR, the line-filtered publication text, and the quality decision remain in JSON for traceability. Reused image-and-crop inputs emit their transcription only once in Markdown, and published OCR is capped at 500 characters per unique image/crop.

DeckContext publishes the package through a sibling staging directory and replaces an existing output only when it is empty or is an intact DeckContext-owned package. Choose a new or empty directory when exporting into a location that contains unrelated files.

The same pipeline is available as a command for repeatable verification or automation:

```powershell
.\DeckContext\DeckContext.Verification.exe "C:\path\input.pptx" "C:\path\deck-context-output"
```

Automation also enables bundled offline OCR by default. Use `--no-ocr` for a faster structure-only extraction:

```powershell
.\DeckContext\DeckContext.Verification.exe "C:\path\input.pptx" "C:\path\deck-context-output" --no-ocr
```

For each deck, both entry points open the PPTX once to build the same IR and capture immutable asset snapshots, then project concise Markdown, complete JSON, diagnostics, manifest, and assets from that result. Duplicate image placements with the same image hash and crop are analyzed once. Without a provider, images are still preserved and the output records one explicit deck-level notice that pixel content was not analyzed.

## Branch Strategy

- `main`: baselined, reviewable project state.
- `dev`: primary integration and day-to-day development branch.

Unless explicitly requested otherwise, future implementation work should target `dev`, not `main`.

## Status

V1 Phases 0–9 are implemented on `dev`. Automated fixture, determinism, partial-degradation, architecture, exporter, pipeline, and view-model evidence runs in Windows CI. Final acceptance remains intentionally open until the packaged application and a representative real deck pass the consolidated Gate A/B/C manual checklist in [`docs/development/v1-acceptance.md`](docs/development/v1-acceptance.md).
