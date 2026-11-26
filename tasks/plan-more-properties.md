# Implementation Plan: More Steel Properties in the Extraction

Source spec: SPEC-more-properties.md (approved 2026-11-26, incl. the `AddMoreProperties` migration)
Task list: tasks/todo-more-properties.md

## Overview
Add 16 chemistry elements and hardness as typed fields, plus a flexible catalog-driven "other measurements" list (impact, tensile details, heat treatment/process, product info), end to end: schema → model/parse/validate → prompt → persistence → review screen → real-browser verification. Built so every task leaves build and tests green, and the app is never left in a state where its model differs from the migration snapshot (EF 9+ throws on `Migrate()` with pending model changes).

## Assumptions taken from the approval (open questions in the spec)
1. The model converts **ppm → weight %** (÷ 10,000) for elements (documented in the prompt; the evidence original shows the converted number, the PDF is beside it for the reviewer).
2. Hardness with several scales: the **first listed**.
3. **No extra sample PDFs yet** → real extraction can only be verified on the 304L sample (nitrogen, cobalt, HB, grain size, reduction ratio, anneal temperature, product form/size, weight). Impact/Charpy and carbon-steel behavior stay **unverified end to end** until more PDFs are provided.
4. Empty optional rows are **collapsed** (required fields always visible).

## Architecture Decisions
- **Schema first, in one migration (`AddMoreProperties`)**: all new `StoredCertificate` columns + the `CertificateMeasurements` table together, so the model and snapshot stay in sync for the rest of the work. Additive only.
- **Typed fields reuse the existing machinery**: `ExtractedField<decimal?>` / `<string>` with the shared `ExtractedValueValidator` rule table (guard tests already force a rule per field and a column length match per text rule).
- **Measurements reuse `ExtractedField<T>` too**: `T = MeasurementValue(Value, Unit, Condition)` — a record, so equality, `OriginalValue`/`IsEdited`, `Confirmed`, page and bbox all come for free. A measurement = key + label + category + that field.
- **Evidence for measurements**: one row per measurement, `FieldName = "Measurement:<key>"` (or `"Measurement:other:<label>"`), `ExtractedValueText` = original rendered `value unit (condition)`. Same unique index.
- **Prompt**: new typed fields are **omitted when absent** (no dozens of nulls); measurements are an array of catalog keys; unknown keys are kept as `other` with their label.
- **Cheap prompt iteration**: a dev command `dotnet run … -- --extract-file <pdf>` prints what the extraction parsed (no DB, no web server), like `--check-llm` — avoids driving the UI for every prompt tweak, and is useful at the office with new certs. (Small addition beyond the spec; mentioned so it isn't a surprise.)
- **Review screen**: typed rows are generated from a descriptor list (not 40 hand-written lines); empty optional rows collapse per section; measurements get their own section with edit/remove/add.
- Each task keeps build at 0 warnings and tests green (scratch `--artifacts-path` builds when an app is running).

## Dependency Graph
```
M1 schema + migration ─┬─ M2 typed model/parse/validate ── M3 typed prompt + --extract-file ─────────┐
                       ├─ M4 measurements model/parse/validate ── M5 measurements prompt ────────────┤
                       └──────────────── M6 persistence: mapper + store (typed + measurements) ──────┤
M2 ── M7 review screen: typed rows + collapse ── M8 review screen: measurements section ── M9 E2E + docs
```

## Task List
Tracked in tasks/todo-more-properties.md.

### Phase 1: Schema
- [x] M1: Entities + `AddMoreProperties` migration (script reviewed)
### Phase 2: Typed properties
- [x] M2: Elements + hardness: model, parse, validator rules
- [x] M3: Prompt for typed fields + `--extract-file` + real check on the sample
### Checkpoint: typed extraction works on the sample (N 0.074, Co 0.29, HB 163)
### Phase 3: Measurements
- [x] M4: Measurement model, catalog, parse, validation
- [x] M5: Prompt for measurements + real check on the sample
### Checkpoint: measurements extracted on the sample; tests green
### Phase 4: Persistence
- [x] M6: Mapper + store for typed fields and measurements (round trip)
### Checkpoint: all tests green; migration applied to Docker DB on next Development start
### Phase 5: UI
- [x] M7: Review screen: typed rows from descriptors + collapsed empty rows
- [x] M8: Review screen: "Other measurements" (edit / remove / add)
### Checkpoint: UI verified in Chrome
### Phase 6: Ship
- [x] M9: Browser E2E incl. save of an edited cert with measurements, DB check, delete MY test row; docs

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| EF `Migrate()` throws on pending model changes if entities change without a migration | High | M1 changes entities and generates the migration together, before any other model/DB change |
| Larger prompt/output hurts accuracy of the existing 20 fields or blows the token budget | High | Re-verify the original 20 fields on the sample in M3 and M5 (regression); keep `max_tokens` headroom; "omit when absent" rule; compare token usage |
| Model invents values for fields not on the cert (hallucination) | High | Prompt: omit, never guess; reviewer confirms everything; evidence keeps originals; check the sample for invented N/Co-like values in absent fields |
| ppm → % conversion done wrongly by the model | Med | Unverifiable on the 304L sample (no ppm values); documented; reviewer sees the PDF side by side; flagged as unverified |
| Only one sample cert → impact/carbon-steel behavior unverified | Med | State plainly; the `--extract-file` tool lets the user check new PDFs quickly; ask for more samples again at the end |
| Review screen becomes very long / hand-written rows explode | Med | Descriptor-driven rows + collapsed empties (M7) |
| Measurement evidence names collide (`other` labels, duplicate keys) | Med | One measurement per key; `other` unique by label (suffix `#2` if the model repeats); tests |
| Collapse refactor regresses verified UI | Med | Verify in Chrome after M7 against the previously recorded 20 rows |
| Migration on the shared Docker DB | Low | Additive (nullable columns + new table); script reviewed before applying; P02V verified after |
| Several real extraction calls cost money | Low | M3, M5, M9 plus UI checks reuse loaded pages; `--extract-file` for prompt tweaks |

## Open Questions
- Provide 2–3 more real PDFs (esp. a carbon-steel cert with Charpy) when convenient — needed to verify impact extraction.
