# Task List — More Steel Properties (see tasks/plan-more-properties.md, SPEC-more-properties.md)

## M1: Schema — entities + `AddMoreProperties` migration — DONE (8 new tests; 182 total pass, 0 warnings; migration generated + SQL reviewed: 18 nullable columns, new CertificateMeasurements table, and a safe widening of CertificateFieldEvidence.FieldName 100→150 (index recreated); NOT yet applied to the Docker DB — applies on next Development start)
**Description:** Add to `StoredCertificate` nullable columns for: Nitrogen, Cobalt, Boron, Aluminum, Titanium, Niobium, Tungsten, Tin, Lead, Arsenic, Antimony, Calcium, Oxygen, Hydrogen, Tantalum, CarbonEquivalent (`decimal(9,4)`), `HardnessValue` (`decimal(9,2)`), `HardnessScale` (`nvarchar(20)`). Add `CertificateMeasurement` entity (Id, CertificateId FK cascade, Key, Label, Category, Value, Unit, Condition) + `DbSet` + config + unique (CertificateId, Key, Label). Generate the migration; review the SQL.
**Acceptance criteria:**
- [ ] Script is additive only: new nullable columns + `CREATE TABLE CertificateMeasurements` (+ index/FK cascade)
- [ ] Existing code unchanged in behavior; model and snapshot in sync
- [ ] Measurement rows round-trip through EF on SQLite
**Verification:** build 0 warnings; tests green; `dotnet ef migrations script` reviewed
**Dependencies:** None
**Files:** `Persistence/StoredCertificate.cs`, `Persistence/CertificateMeasurement.cs`, `Persistence/CertDbContext.cs`, `Persistence/Migrations/*`, `tests/.../MeasurementPersistenceTests.cs`
**Scope:** Medium

## M2: Typed fields — model, parsing, validator rules — DONE (50 new tests; 232 total pass, 0 warnings; guard tests pass: every new field has a rule, HardnessScale length matches the column; also added a lenient number reader so a trace value like "<0.001"/"ND" no longer fails the whole extraction — applies to ALL numeric fields)
**Description:** Add the 16 elements to `ChemistryComposition` and `HardnessValue` (decimal?) / `HardnessScale` (string) to `MechanicalProperties`; Raw DTOs + mapping in `CertExtractionService` (sets `OriginalValue`); validator rules (elements 0–100; hardness > 0 and ≤ 2000; scale ≤ 20 chars = column size).
**Acceptance criteria:**
- [ ] Parser maps the new fields; absent → null object; null value stays "not found"
- [ ] Guard tests pass: every extractable field has a rule; text rule lengths match EF columns
- [ ] Existing 20 fields and tests unchanged
**Verification:** parser + validator tests; full suite green
**Dependencies:** M1
**Files:** `Models/CertExtractionModels.cs`, `Services/CertExtractionService.cs`, `Services/ExtractedValueValidator.cs`, `tests/.../CertExtractionServiceTests.cs`, `ExtractedValueValidatorTests.cs`
**Scope:** Medium

## M3: Prompt for typed fields + `--extract-file` + real check — DONE (25 new tests; 257 total pass, 0 warnings; REAL runs on the sample, twice, identical: nitrogen 0.074, cobalt 0.29, hardness 163 HB all with boxes; the original 20 fields unchanged; none of the 14 absent elements invented; response ~550 tokens vs 8000 max. Highlight POSITIONS still to be eyeballed in the UI at M7)
**Description:** Update `CertExtractionPrompts` schema for the new typed fields (omit when absent; elements in weight %; ppm → % conversion rule; hardness value + scale, first listed). Add `dotnet run … -- --extract-file <pdf>` that runs the real extraction and prints every parsed field (label, value, page, has-box) — no DB/web server. Run it on the sample.
**Acceptance criteria:**
- [ ] Sample returns nitrogen 0.074, cobalt 0.29, hardness 163 / HB with plausible boxes
- [ ] The original 20 fields still match the earlier extraction (no regression); no invented values for absent elements
- [ ] Token usage within `max_tokens`
**Verification:** one real extraction via the CLI; unit test for the printer; build clean
**Dependencies:** M2
**Files:** `Services/CertExtractionPrompts.cs`, `Services/ExtractionReport.cs` (new), `Program.cs`, `tests/.../ExtractionReportTests.cs`
**Scope:** Medium

### Checkpoint: After M1–M3 — typed extraction works on the sample; tests green; review with human

## M4: Measurements — model, catalog, parsing, validation — DONE (70 new tests; 327 total pass, 0 warnings; deviation from plan: a repeated "other" label keeps the FIRST listed rather than adding a "#2" suffix, matching "one value per property"; long values are kept as printed — the store flags them, nothing is silently truncated; only "other" labels are cut to 100 chars)
**Description:** `MeasurementValue(Value, Unit, Condition)` record; `ExtractedMeasurement` (Key, Label, Category, `ExtractedField<MeasurementValue>`); `MeasurementCatalog` (keys → label + category); `SteelCertificate.Measurements`; Raw DTO `measurements` array; parse (unknown key → `other` with label; malformed entry skipped; duplicates: one per key, `other` unique by label); validator for lengths (value ≤ 200, unit ≤ 20, condition ≤ 200, label ≤ 100) matching M1 columns.
**Acceptance criteria:**
- [ ] Catalog + guard tests (every catalog key has category/label; lengths match EF columns)
- [ ] Parser tolerates missing/malformed `measurements`
- [ ] `IsEdited` derivation works for `MeasurementValue`
**Verification:** unit tests; suite green
**Dependencies:** M1
**Files:** `Models/CertExtractionModels.cs` (or `Models/Measurements.cs`), `Services/CertExtractionService.cs`, `Services/ExtractedValueValidator.cs`, `tests/.../MeasurementTests.cs`
**Scope:** Medium

## M5: Prompt for measurements + real check — DONE (13 new tests; 340 total pass, 0 warnings; 3 real runs on the sample. Stable core: product form, product size, weight, grain size 6-8, intergranular corrosion test, heat treatment (solution anneal 1900F min). Varies run to run: melt practice, reduction ratio, test direction, mill name, elongation gauge length, quantity presence. Bug found + fixed: my prompt example "6 pieces" leaked → model returned quantity 6 from the Mark column; examples replaced, quantity now restricted to Pieces/Qty columns → returns 1 from the Pieces column; added a guard test against sample-derived examples. No impact values invented; original 20 + typed fields unchanged; response ~900 tokens vs 8000 max. Highlight POSITIONS to be eyeballed in the UI at M7)
**Description:** Add the `measurements` schema + catalog keys to the prompt (units/conditions as printed; `other` with label; omit when absent). Run `--extract-file` on the sample.
**Acceptance criteria:**
- [ ] Sample yields measurements for items actually printed (e.g. grain size 6-8, reduction ratio 7.0:1, anneal temperature, product form/size, weight) with correct page/boxes
- [ ] No invented impact/other values; the original 20 + typed fields still correct (regression)
**Verification:** one real extraction via the CLI
**Dependencies:** M3, M4
**Files:** `Services/CertExtractionPrompts.cs`, (`ExtractionReport.cs`)
**Scope:** Small

### Checkpoint: After M4–M5 — measurements extracted on the sample; tests green; review with human

## M6: Persistence — mapper + store (typed + measurements) — DONE (30 new tests; 370 total pass, 0 warnings; SQLite round trips for new typed fields + measurements incl. edited/added/removed; migration AddMoreProperties still NOT applied to the Docker DB — applies on the next Development start (M7's Chrome run))
**Description:** Mapper fills the new columns and evidence rows (original/edited/confirmed rules as today); measurements → `CertificateMeasurement` rows + evidence `Measurement:<key>`; store re-validates new typed values and measurement lengths; round-trip tests.
**Acceptance criteria:**
- [ ] Typed values in columns, NULL (never 0) when missing; evidence originals/Edited/Confirmed correct
- [ ] Measurement edited/added/removed → final row (or none) + evidence row with original `value unit (condition)` and `Edited`
- [ ] Out-of-range/over-long → `Invalid`; unedited existing certs save exactly as before
**Verification:** mapper + store tests (SQLite); full suite green
**Dependencies:** M2, M4
**Files:** `Persistence/CertificateMapper.cs`, `Persistence/CertificateStore.cs`, `tests/.../CertificateMapperTests.cs`, `CertificateStoreTests.cs` / new test file
**Scope:** Medium

### Checkpoint: After M6 — all tests green; review with human

## M7: Review screen — typed rows + collapsed empties — DONE (build 0 warnings; 370 tests pass; verified in Chrome with a real extraction: 23 visible rows = original 20 minus empty Vanadium + Nitrogen/Cobalt/Hardness/Scale; all original values unchanged; "15 not found on this certificate — show" toggles to 38 rows; add/edit/clear/revert interplay with collapse verified; highlights land exactly on .074, .29 and 163.0. Extra, needed because of the longer list: PDF pane is now sticky (verified in the real stylesheet; disabled on the narrow stacked layout). Migration AddMoreProperties APPLIED to the Docker CertDb at app start and verified: 6/6 sampled new columns, CertificateMeasurements table, evidence FieldName widened to 150, P02V row intact 19/0/2)
**Description:** Replace hand-written rows with a descriptor list per section (typed fields incl. new ones); collapse optional empty rows into "N not found on this certificate — show"; required fields always visible. Behavior of existing rows unchanged.
**Acceptance criteria:**
- [ ] Chrome: the original 20 rows behave as before (values, checkboxes, highlight, edit/add/clear/revert)
- [ ] New N/Co/hardness rows appear with values, highlights, editing; empty ones collapsed with a correct count and expandable (each still has "add")
**Verification:** build clean; one real extraction in Chrome
**Dependencies:** M2 (M3 for real data)
**Files:** `Components/CertReviewPage.razor`, `wwwroot/css/cert-review.css`
**Scope:** Medium

## M8: Review screen — "Other measurements" — DONE (31 new tests for the rules in MeasurementEditor; 401 total pass, 0 warnings; verified in Chrome with a real extraction (11 measurements listed in category order): hover highlight, edit (un-ticks, "edited" badge + original tooltip), per-part inline errors, Esc, revert, remove (shows "removed" + revert), add from the catalog (13-option picker, validation, new row has "Model extracted: nothing"), add custom label, duplicate-label rejection (case-insensitive), removing reviewer-added measurements leaves no trace, Save disabled while an editor/picker is open; server log had 0 errors. Not yet checked: saving to the DB (M9). Note: highlights on inline running text (grain size) are looser than table-cell boxes)
**Description:** Section listing measurements (`label: value unit (condition)`), checkbox, highlight, edit (value/unit/condition inputs), revert, edited marker, **remove**, and **Add measurement** (catalog pick or custom label). Same rules: edit un-ticks confirm, read-only after save, Save disabled while an editor is open.
**Acceptance criteria:**
- [ ] Spec success criterion 4 in Chrome (edit, remove, add from catalog, add custom, revert)
- [ ] Validation lengths enforced inline; duplicates of a catalog key prevented
**Verification:** build clean; Chrome walkthrough
**Dependencies:** M4, M5, M7
**Files:** `Components/CertReviewPage.razor`, `wwwroot/css/cert-review.css`, (a small helper class)
**Scope:** Medium

### Checkpoint: After M7–M8 — UI verified in Chrome; review with human

## M9: Browser E2E + docs — DONE (real extraction of the sample in Chrome; test heat `M9-TEST-PROPS` (heat number edited from P02V so it wouldn't collide); edits: heat number, hardness 163→165, grain size 6-8→7, Test direction removed, Impact energy "54 J (-40 C)" added; ticked Heat, Grade, Carbon, Nitrogen, Cobalt, Hardness, Grain size, Product form + the added impact. Saved. DB: NitrogenPct .0740, CobaltPct .2900, HardnessValue 165.00 / HB; 11 `CertificateMeasurements` rows (grain size 7, impact 54/J/-40 C, no test_direction row); evidence 35 rows, 5 edited (heat number, hardness, grain size, impact, test direction), 9 confirmed; evidence originals correct (HardnessValue 163.0, grain 6-8, impact NULL, test direction L, N 0.074, Co 0.29). Then deleted ONLY that row (cascade cleared its evidence + measurements). P02V intact 19 evidence / 0 edited / 2 confirmed / 0 measurements. Server log: 0 fail/crit/exception. Full suite 401 pass. Not verified: impact/Charpy on a real cert, ppm→% conversion, carbon-steel certs, LiteLLM/Bedrock.)
**Description:** Extract the sample; edit typed values and measurements; save with a clearly named test heat number; query the DB (typed columns, `CertificateMeasurements`, evidence flags); delete ONLY my test row; update CLAUDE.md, spec status, trackers; restate what remains unverified (impact, ppm).
**Acceptance criteria:**
- [ ] Spec success criteria 1–9 verified or explicitly flagged as unverified
- [ ] My test row deleted; P02V untouched; migration applied cleanly
**Verification:** DB queries; full build + test run
**Dependencies:** M6, M8
**Files:** `CLAUDE.md`, `SPEC-more-properties.md`
**Scope:** Small

### Checkpoint: Complete
