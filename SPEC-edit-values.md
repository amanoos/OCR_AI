# Spec Delta: Edit Extracted Values Before Saving

Status: implemented and verified (2026-11-26) — 174 unit tests; migration `AddEvidenceEdited` applied to the Docker `CertDb`; edit/add/clear/revert/validation flows exercised in Chrome; an edited certificate (test heat `E7-TEST-EDIT`) was saved and the DB rows checked (final values, original text, `Edited`/`Confirmed` flags, NULL handling), then that test row was deleted. Success criterion 7 (edited heat number → duplicate) is covered by unit tests only, not in the browser.
Deviations from this spec: dates may be up to one day ahead of today (UTC) to tolerate time zones; tooltips are plain `title` attributes (MudTooltip crashes in the interactive island).

## Objective
Today a reviewer can only confirm or not confirm what the model extracted. If the model misreads a number (or misses a value that is plainly on the PDF), the only options are to save a wrong value unconfirmed or not save at all. Let the reviewer **correct any value, add a missing one, or clear a wrong one** on the review screen, while keeping a record of what the model originally said — so a corrected value is never mistaken for a machine-extracted one.

Decisions made (2026-11-26): all fields editable including empty ones; an edit un-ticks the confirm box (re-confirm required); the original is kept and the row marked edited; validation is type + sane ranges.

Inherits everything else from SPEC.md, SPEC-persistence.md and SPEC-office-readiness.md.

## Current Behavior
- The review page shows each extracted field read-only with a confirm checkbox (only when a value exists).
- `ExtractedField<T>` has `Value`, `PageNumber`, `BoundingBox`, `Confirmed`. Omitted fields are `null` objects; a missing date is a `null` field (it can't hold "no value").
- Saved evidence rows record the value as extracted, page, bounding box and `Confirmed`.
- `MissingRequiredFields` is computed once at extraction time.

## New Behavior

### Review screen
- Every field row has an **edit** control (pencil; for a field with no value, "add"). Clicking it switches that row to an inline text input with **Apply** and **Cancel**; Enter applies, Esc cancels.
- **Validation** (inline error under the input, Apply blocked until valid):
  - numbers must parse (invariant culture, `.` decimal) — chemistry % and elongation % must be 0–100; yield/tensile strength must be > 0 and ≤ 1,000,000 psi;
  - cert date must be `YYYY-MM-DD`, not before 1950-01-01 and not in the future;
  - Heat Number and Grade can be changed but not cleared to empty (the save rule requires them);
  - text fields are trimmed; optional text/number fields may be cleared (meaning "the model's value was wrong / not on the cert").
- **Applying an edit un-ticks the confirm checkbox** — the corrected value must be explicitly confirmed again.
- An edited row shows an **"edited" marker**; its tooltip shows what the model originally extracted ("none" for an added value); a **revert** control restores the original value (and the row stops being "edited").
- Hover highlighting still uses the original bounding box when there is one; an added value has no box (nothing highlighted).
- The header's "Missing: …" warning is computed from the **current** values (adding a heat number clears it).
- Rows are read-only while a save is in flight and after a successful save.

### Data model
- `ExtractedField<T>` gains `OriginalValue` (what the model said, set when mapped from the model response). **"Edited" is derived**: `Value` differs from `OriginalValue` (so reverting clears it automatically; text compared trimmed).
- `CertDate` becomes `ExtractedField<DateOnly?>` (same fix previously made for decimals) so a date can be cleared.
- Omitted fields: the editor creates the `ExtractedField` object on first add, with `OriginalValue = null`, page 0, no box.

### Persistence
- `StoredCertificate` columns hold the **final** (possibly edited) values, as today.
- `CertificateFieldEvidence`:
  - `ExtractedValueText` now always means **what the model extracted** (NULL if it extracted nothing);
  - new column **`Edited` (bit, NOT NULL, default 0)** — true when the final value differs from the original;
  - an evidence row is written when the field has a value **or** was edited to empty (so a cleared value leaves a trace); a never-extracted, never-added field still gets no row.
  - `Confirmed` stays per field; an edited field is only `Confirmed` if the reviewer ticked it again.
- **One new migration** (`AddEvidenceEdited`) — existing rows (e.g. the saved P02V test row) get `Edited = 0`.
- Store validation (defense in depth, not just the UI): numeric ranges and the date range above are re-checked on save; a violation returns `Invalid` with reasons (e.g. "Carbon must be between 0 and 100."). Required-field rule unchanged (Heat Number + Grade present and confirmed).

## Success Criteria
1. Editing a numeric or text value on the review screen changes the displayed value, un-ticks its confirm box and shows the "edited" marker with the original in the tooltip.
2. An invalid entry (non-number, out-of-range, bad date, cleared Heat Number/Grade) shows an inline error and is not applied.
3. A missing value (e.g. Vanadium) can be added; an extracted value can be cleared; revert restores the original and removes the marker.
4. Saving an edited certificate stores the **final** value in the certificate columns and, in the evidence row, the **original** text with `Edited = 1`; an added value has `ExtractedValueText` NULL and `Edited = 1`; a cleared value stores NULL in the column with an evidence row (original text, `Edited = 1`); unedited rows have `Edited = 0`.
5. An edited field that wasn't re-confirmed is saved with `Confirmed = 0`; the heat number/grade rule still blocks saving unless both are confirmed.
6. The store rejects out-of-range values with `Invalid` even if the UI was bypassed.
7. Editing the Heat Number to one that already exists is reported as a duplicate on save (no overwrite).
8. The migration script shows `Edited bit NOT NULL DEFAULT 0` and nothing else; `dotnet build` 0 warnings; all tests pass.

## Testing Strategy
- Unit tests: the validator (parse, ranges, date), `ExtractedField` edited derivation, mapper (edited / added / cleared / unedited evidence), store (range rejection, edited save round-trip on SQLite), `CertDate` nullable change (existing tests updated).
- Migration: review the generated SQL script; applied by Development auto-migrate to the Docker `CertDb` (existing row keeps working).
- Browser end-to-end in Chrome (one extraction call): edit a number, add vanadium, clear a value, revert, try invalid input, then save with the heat number changed to a test value; read the DB rows to confirm `Edited` and original text; **then delete my own test row** (only that row — the earlier P02V row is left alone).

## Boundaries
- Always: keep the original value; un-confirm on edit; validate on the server side too; keep rows read-only after save.
- Ask first: further schema changes beyond the one `Edited` column; new NuGet packages (none expected).
- Never: auto-confirm an edited value; overwrite an existing saved certificate; drop the original value of an edited field.

## Out of Scope (Deferred)
- Editing a certificate after it has been saved (needs the saved-certificates page); delete/versioning.
- A full per-edit history (only original vs final is kept).
- Bulk edit, undo/redo stacks, changing/redrawing bounding boxes, re-running extraction.
- Adding nitrogen / cobalt / hardness fields (separate item, gap 11 in CLAUDE.md).
- Recording *who* edited (no authentication yet).

## Open Questions
1. Dates must be entered as `YYYY-MM-DD` (unambiguous; matches the display). OK?
2. Range defaults above (chemistry/elongation 0–100 %, strengths > 0 and ≤ 1,000,000 psi, dates 1950 → today). Adjust any?
