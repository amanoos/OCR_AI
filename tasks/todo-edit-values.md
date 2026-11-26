# Task List — Edit Extracted Values (see tasks/plan-edit-values.md, SPEC-edit-values.md)

## E1: `OriginalValue` / `IsEdited`; `CertDate` nullable; originals set at mapping — DONE (15 new tests; 94 total pass, 0 warnings)
**Description:** Add `OriginalValue` and derived `IsEdited` to `ExtractedField<T>`; change `CertDate` to `ExtractedField<DateOnly?>`; `CertExtractionService` sets `OriginalValue = Value` when mapping each field; fix every ripple (razor date row, mapper, tests).
**Acceptance criteria:**
- [ ] `IsEdited` false right after mapping; true when `Value` differs (strings compared trimmed); false again when set back
- [ ] `CertDate` can hold "no value"; an unparseable model date still maps to an empty/missing date
- [ ] No behavior change in extraction/UI/save for unedited data
**Verification:** new `ExtractedField` tests + existing suite green; build 0 warnings
**Dependencies:** None
**Files:** `Models/CertExtractionModels.cs`, `Services/CertExtractionService.cs`, `Components/CertReviewPage.razor`, `Persistence/CertificateMapper.cs`, existing tests (mapper/store/service)
**Scope:** Medium (type ripple)

## E2: `ExtractedValueValidator` — DONE (53 new test cases; 147 total pass, 0 warnings)
**Description:** Pure static validator with a rule table keyed by field name: type (text/decimal/date), required, max length (= DB column size), numeric range, date range (1950-01-01 … today). Parse numbers with invariant culture; dates strictly `yyyy-MM-dd`.
**Acceptance criteria:**
- [ ] Chemistry % and elongation 0–100; yield/tensile > 0 and ≤ 1,000,000; dates 1950-01-01…today; Heat Number/Grade required
- [ ] Text trimmed; max lengths match `CertDbContext` column sizes
- [ ] Returns a parsed value or a human-readable error; blank optional input = "clear"
**Verification:** unit tests over each rule + boundary values; build clean
**Dependencies:** None
**Files:** `Services/ExtractedValueValidator.cs`, `tests/.../ExtractedValueValidatorTests.cs`
**Scope:** Small–Medium

### Checkpoint: After E1–E2 — build 0 warnings, tests green, review with human

## E3: Evidence `Edited` + mapper rules + migration — DONE (13 new tests; 160 total pass, 0 warnings; migration `AddEvidenceEdited` generated and its SQL reviewed — NOT yet applied to the Docker DB, that happens on the next Development start)
**Description:** Add `Edited` to `CertificateFieldEvidence` (+ `HasDefaultValue(false)`); mapper writes `ExtractedValueText` from `OriginalValue`, sets `Edited = IsEdited`, and writes a row when the field has a value OR was edited to empty; generate migration `AddEvidenceEdited` and review its SQL.
**Acceptance criteria:**
- [ ] Script contains only `ALTER TABLE … ADD [Edited] bit NOT NULL DEFAULT CAST(0 AS bit)` (+ history row)
- [ ] Mapper: unedited → original text, `Edited=0`; edited → original text, `Edited=1`; added → NULL original, `Edited=1`; cleared → column NULL + evidence row `Edited=1`; never-extracted untouched field → no row
**Verification:** mapper tests; `dotnet ef migrations script` reviewed; build clean
**Dependencies:** E1
**Files:** `Persistence/CertificateFieldEvidence.cs`, `Persistence/CertDbContext.cs`, `Persistence/CertificateMapper.cs`, `Persistence/Migrations/*`, `tests/.../CertificateMapperTests.cs`
**Scope:** Medium

## E4: Store re-validates + edited round trip — DONE (14 new tests; 174 total pass, 0 warnings; existing store tests unchanged)
**Description:** `CertificateStore` runs the validator on the final values (ranges, dates, max lengths) in addition to the required-field rule; failures → `Invalid` with reasons. Add SQLite round-trip tests for edited/added/cleared.
**Acceptance criteria:**
- [ ] Out-of-range or over-long final values → `Invalid`, nothing saved
- [ ] Edited save stores final value in the column and original+`Edited=1` in evidence; added/cleared as specced
- [ ] Unedited certs save exactly as before (existing store tests unchanged)
**Verification:** store tests (SQLite in-memory) green
**Dependencies:** E2, E3
**Files:** `Persistence/CertificateStore.cs`, `tests/.../CertificateStoreTests.cs`
**Scope:** Medium

### Checkpoint: After E3–E4 — all tests green; migration reviewed; review with human

## E5: Generic review row (refactor only) — DONE (build 0 warnings; verified in Chrome: all 20 rows identical to before, hover highlight and checkboxes work; migration AddEvidenceEdited applied to Docker CertDb at app start, P02V row intact with Edited=0)
**Description:** Replace `RenderField`/`RenderDecimalField`/`RenderDateField` with one generic row renderer in `CertReviewPage.razor` (label, field accessor, formatter). **No behavior change.**
**Acceptance criteria:**
- [ ] Every field renders exactly as before (value, "—" for missing, checkbox only with a value, hover highlight)
- [ ] Browser check on the sample cert: all rows, checkboxes and highlights unchanged
**Verification:** build clean; Chrome check (one extraction call)
**Dependencies:** E1
**Files:** `Components/CertReviewPage.razor`
**Scope:** Small–Medium

## E6: Inline editing — DONE (build 0 warnings; 174 tests pass; verified in Chrome: edit/Enter/Esc, un-tick on edit, edited badge + original tooltip, add to empty field, clear, revert, inline errors for non-number/out-of-range/bad date/empty Heat Number, Save disabled while an editor is open, highlight kept on edited rows. Not yet saved to DB — that is E7. Bug found+fixed during the run: MudTooltip crashed the interactive island (needs the popover provider) → replaced with plain `title` tooltips)
**Description:** Per row: edit/add control → inline input with Apply/Cancel (Enter/Esc); validate with `ExtractedValueValidator`; apply un-ticks confirm; "edited" marker with original in tooltip and a revert control; omitted fields get an `ExtractedField` created on first add; header "Missing:" computed live; rows read-only while saving/after saved.
**Acceptance criteria:**
- [ ] Spec success criteria 1–3 and 7 behave in Chrome (edit number, add vanadium, clear, revert, invalid inputs blocked with inline error)
- [ ] Heat Number/Grade cannot be cleared; editing never auto-confirms
**Verification:** build clean; Chrome walkthrough
**Dependencies:** E2, E5
**Files:** `Components/CertReviewPage.razor`, `wwwroot/css/cert-review.css`
**Scope:** Medium

### Checkpoint: After E5–E6 — build clean, edit flow works in the browser, review with human

## E7: Browser E2E + docs — DONE (saved an edited cert in Chrome; DB verified; my test row deleted, P02V untouched 19/0/2; CLAUDE.md + spec updated)
**Description:** Extract the sample, perform edit/add/clear/revert/invalid, change the heat number to a clearly named test value, confirm and save; read the DB (Edited flags, original text, NULLs); delete ONLY my test row; update CLAUDE.md and the spec status.
**Acceptance criteria:**
- [ ] Spec success criteria 4, 5, 6 (via tests), 8 verified; DB shows expected evidence rows
- [ ] My test row deleted; P02V row untouched (counts verified before/after)
- [ ] CLAUDE.md + SPEC-edit-values.md + trackers updated
**Verification:** DB queries; full build + test run
**Dependencies:** E4, E6
**Files:** `CLAUDE.md`, `SPEC-edit-values.md`
**Scope:** Small

### Checkpoint: Complete — all spec success criteria met
