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

## Branch Strategy

- `main`: baselined, reviewable project state.
- `dev`: primary integration and day-to-day development branch.

Unless explicitly requested otherwise, future implementation work should target `dev`, not `main`.

## Status

Architecture and V1 requirement baseline established. Implementation has not started yet.
