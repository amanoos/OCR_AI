# Spec Delta: More Steel Properties in the Extraction

Status: implemented and verified (2026-11-26) on the 304L sample; criteria 1–9 met, with these caveats: impact/Charpy extraction, ppm→% conversion and carbon-steel certs are NOT verified against real PDFs (only the one 304L sample exists); optional measurements vary run to run (melt practice, reduction ratio, mill name, gauge length, test direction); highlights on inline running text are looser than on table cells. Details: tasks/todo-more-properties.md

## Objective
The extraction captures only 20 fields. A typical mill test report prints more — on the sample 304L cert alone: **nitrogen** (.074), **cobalt** (.29), **Brinell hardness** (163 HB), a **grain size**, a **reduction ratio**, the **solution-anneal temperature**, the **product form and dimensions** — and none of it is extracted, shown or stored. Add the properties modern certs commonly report, so the review screen and database reflect the whole certificate, with the same guarantees as today: nothing is trusted until a person confirms it, edits keep the model's original, and everything is validated.

Decisions made (2026-11-26): **hybrid storage** (typed columns for uniform, high-value properties; a flexible table for properties whose shape varies); groups added this round: **more chemistry elements, hardness, impact/Charpy, tensile details / heat treatment / product info**; **units and values exactly as printed**; **one value per property** (no per-specimen lists).

Inherits everything else from SPEC.md, SPEC-persistence.md, SPEC-edit-values.md and SPEC-office-readiness.md.

## Current Behavior
- `CertExtractionPrompts` asks for 20 fixed fields; `ChemistryComposition` has 10 elements, `MechanicalProperties` has yield/tensile/elongation/reduction of area (text).
- Every extractable field has: a DTO property, a domain property, a mapper line, a `StoredCertificate` column, a validator rule, a review-screen row. Adding a field touches all of them.
- Review screen shows every field, including empty ones (e.g. Vanadium "—").

## New Behavior

### A. Typed properties (new columns, validator rules, review rows, edit flow)
**More chemistry elements** (weight %, 0–100, `decimal(9,4)` like the existing ones): Nitrogen, Cobalt, Boron, Aluminum, Titanium, Niobium (a.k.a. Columbium), Tungsten, Tin, Lead, Arsenic, Antimony, Calcium, Oxygen, Hydrogen, Tantalum, and Carbon Equivalent (a computed value some certs print; same range rule).
- Elements are always weight %. If the cert prints **ppm**, the model converts to % (÷ 10,000) — same precedent as strengths normalized to psi. *(See Open Question 1.)*

**Hardness** (one value per cert): `HardnessValue` (> 0 and ≤ 2000) and `HardnessScale` (text as printed, e.g. `HB`, `HBW`, `HRB`, `HRC`, `HV`; ≤ 20 chars). If several readings/scales are printed, the model reports the first listed / the one reported as the product's hardness.

### B. Flexible "other measurements" (new table, catalog-driven rows)
For properties whose shape varies. Each measurement = **key, label, category, value (text as printed), unit (as printed), condition (as printed)** + page, bounding box, confirmed — i.e. an `ExtractedField<MeasurementValue>` so it reuses the existing original/edited/confirmed machinery.

Catalog (keys the prompt offers the model; the model may also return `other` with the printed label for anything else, max 10):
| Category | Keys |
|---|---|
| Impact | `impact_energy`, `impact_test_temperature`, `impact_specimen_size`, `impact_lateral_expansion`, `impact_shear_area` |
| Tensile details | `yield_offset`, `elongation_gauge_length`, `test_direction`, `specimen_type` |
| Heat treatment & process | `heat_treatment`, `anneal_temperature`, `melt_practice`, `grain_size`, `ferrite_content`, `intergranular_corrosion_test`, `reduction_ratio` |
| Product & traceability | `product_form`, `product_size`, `lot_number`, `quantity`, `weight`, `mill_name` |

- One measurement per key (one value per property); `other` entries are unique by label.
- Value ≤ 200 chars, unit ≤ 20, condition ≤ 200, label ≤ 100. Free text — **no numeric range checks** (shapes vary); lengths only.

### C. Extraction prompt and parsing
- The prompt schema gains the new typed fields (omitted when absent — the model should **leave out** fields not on the document rather than return dozens of nulls) and a `measurements` array of `{key, label?, value, unit?, condition?, page, bbox}`.
- Parser: unknown/missing → absent; unknown measurement keys are kept as `other` with their label rather than dropped; malformed entries skip that entry, not the whole response.
- Token budget stays within `max_tokens` (check on the sample).

### D. Review screen
- New typed fields appear in their sections (Chemistry, Mechanical) with the same row behavior: value, confirm checkbox, hover highlight, **inline edit / add / clear / revert**, validation, edited marker.
- **Optional rows with no value are collapsed** per section into one line ("N not found on this certificate — show") that expands to the empty rows (each still has "add"); Heat Number and Grade stay always visible. *(This changes today's always-visible empty rows — Open Question 4.)*
- New **"Other measurements"** section: one row per measurement showing `label: value unit (condition)`, confirm checkbox, hover highlight, edit (three small inputs: value / unit / condition), revert, edited marker, and **remove** (a wrongly extracted measurement); plus **"Add measurement"** (pick a catalog key or enter a custom label).
- Same rules as before: an edit un-ticks confirm; read-only while saving / after saved; Save disabled while an editor is open.

### E. Persistence
- `StoredCertificate`: new nullable columns for each new element (`decimal(9,4)`), `HardnessValue decimal(9,2)`, `HardnessScale nvarchar(20)`.
- New table **`CertificateMeasurements`** (Id, CertificateId FK cascade, Key, Label, Category, Value, Unit, Condition) holding the **final** measurements.
- Evidence rows (same semantics as SPEC-edit-values): typed fields as today; each measurement gets an evidence row `FieldName = "Measurement:<key or label>"` with `ExtractedValueText` = the model's original rendered as `value unit (condition)`, `Edited`, `Confirmed`, page, bbox. A removed measurement leaves an evidence row (original text, `Edited=1`); an added one has a NULL original. Unique (CertificateId, FieldName) preserved.
- **One new migration** (`AddMoreProperties`), additive: new nullable columns + new table. Existing rows (P02V) unaffected.
- Store re-validates all new values with the shared validator (same ranges/lengths) before saving.

## Success Criteria
1. On the sample 304L cert, the extraction returns **nitrogen 0.074, cobalt 0.29, hardness 163 with scale HB**, and the review screen shows them with correct highlights; other printed items (grain size 6-8, reduction ratio 7.0:1, anneal temperature, product form/size, weight) appear as measurements where the model finds them.
2. The sample's previously-extracted 20 fields are unchanged (no regression).
3. New typed fields can be edited/added/cleared/reverted with the same validation as existing ones; invalid input is blocked inline.
4. Measurements can be edited (value/unit/condition), removed, and added from the catalog or with a custom label; edited/removed/added ones are marked and keep originals.
5. A saved certificate stores new typed values in columns and measurements in `CertificateMeasurements`; evidence rows record originals, `Edited` and `Confirmed` correctly (verified by DB query); NULL — never 0 — for missing numbers.
6. The store rejects out-of-range or over-long new values with `Invalid`, even if the UI is bypassed.
7. Empty optional rows are collapsed with a count and expandable; required fields stay visible.
8. Migration script reviewed (additive only); applied to the Docker `CertDb`; P02V row intact.
9. `dotnet build` 0 warnings; all tests pass; extraction of the sample stays within token limits.

## Testing Strategy
- Unit: parser (new fields, `measurements`, malformed entries, omitted fields, ppm already converted by the model — parser just reads numbers), validator rule table (guard test extended: every extractable field and every measurement kind has rules), mapper (typed + measurement evidence incl. edited/added/removed), store (validation, round trip on SQLite), `MeasurementValue` equality/edited derivation.
- Migration: review the SQL script; apply via Development auto-migrate; verify existing row.
- Browser (Chrome, real extraction of the sample): confirm criterion 1–4, 7; save an edited cert with a clearly named test heat number; query the DB; delete **only my test row**.
- **Real-world breadth is limited by available samples** — only one cert (304L stainless MTR) is on hand. Charpy/impact and carbon-steel certs can't be verified end to end until more PDFs are provided (Open Question 3).

## Boundaries
- Always: keep values/units as printed (except ppm → %); omit rather than guess; keep original/edited/confirmed semantics; validate on save.
- Ask first: extending the catalog beyond the keys above; changing the extraction prompt's existing fields; new NuGet packages (none expected).
- Never: auto-confirm; convert units other than ppm → %; invent a value the cert doesn't print; store 0 for a missing number.

## Out of Scope (Deferred)
- Multiple readings per property (3 Charpy specimens, several hardness readings) — one value per property this round.
- Unit conversion (SI ↔ US customary) beyond ppm → %.
- Automatic compliance checking against grade/spec limits (e.g. "is this 304L within ASTM chemistry?").
- Vendor-specific prompt templates; multiple lots/products per certificate (beyond one cert per heat).
- Searching/filtering saved certs by these properties (needs the saved-certificates page).

## Open Questions
1. **ppm → %:** boron, oxygen, hydrogen, nitrogen are often printed in ppm. OK for the model to convert to weight % (÷ 10,000) so they fit the typed `%` columns? (The alternative is to capture them as measurements with their printed unit.)
2. **Hardness with several scales** printed (e.g. HB and HRB): capture the first listed — OK?
3. **More sample certs:** can you provide 2–3 more real PDFs (ideally a carbon-steel/structural cert with Charpy impact values) so we can verify beyond the 304L sample?
4. **Collapsing empty rows** changes today's always-visible "—" rows (e.g. Vanadium). OK?
