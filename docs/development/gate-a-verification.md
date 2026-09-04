# Gate A Verification — Native Charts and Embedded Excel

Phase 5 requires a real Windows review because production PPTX files contain chart/workbook combinations that small fixtures cannot fully represent. Use the commit-traceable GitHub Actions artifact; no SDK, Visual Studio, or repository clone is required.

## Run

1. Download and unzip `DeckContext-dev-win-x64-{short-sha}`.
2. Open PowerShell in the unzipped directory.
3. Run:

```powershell
.\DeckContext\DeckContext.Verification.exe `
  "C:\path\Ecuador_FBB_Plan_Competitive_Analysis_v4_Add_NormalPrice_CN.pptx" `
  "C:\path\deck-context-output"
```

The command produces the complete V1 context package:

- `deck.context.md` — human/LLM-readable deck, slide, object, chart, and workbook representation;
- `deck.context.json` — normalized deck, chart, workbook, worksheet, range, cell, status, and diagnostic data;
- `extraction-report.json` — flattened diagnostic evidence;
- `manifest.json` — asset provenance, paths, sizes, and hashes;
- `workbooks\*.xlsx` — exact embedded workbook bytes verified against the SHA-256 stored in JSON.

## Review checklist

- The command exits successfully and reports the expected slide count.
- The five native charts in the Ecuador sample retain chart type, series, category/value caches, and source formulas.
- Each internally linked chart exposes `embeddedWorkbook`, including chart/workbook part URIs, relationship id, size, SHA-256, and worksheet metadata.
- Each supported A1 source formula points to a deterministic `range-###`; series name, category, and value bindings reference those range ids.
- Range cells preserve their address, presence, data type, optional style index, optional formula, raw value, and resolved value.
- Exported `.xlsx` files open normally and correspond to the chart workbooks.
- Any cache/cell disagreement appears as `DCX-WORKBOOK-CACHE-MISMATCH`; no value is silently repaired or invented.
- A workbook-specific failure marks only its chart/deck path partial and leaves native chart cache evidence available.

Markdown representation is now included. Complete this review together with Gate B and Gate C using `v1-acceptance.md` so a single real-deck run validates the data chain, desktop workflow, and LLM usefulness.
