# Implementation Plan: Edit Extracted Values Before Saving

Source spec: SPEC-edit-values.md (approved 2026-11-26, incl. the `Edited` column migration)
Task list: tasks/todo-edit-values.md

## Overview
Let the reviewer correct, add or clear any field on the review screen, keep the model's original value, mark what changed, and enforce the same validation again in the store. Built bottom-up: data model → validator → persistence → store → UI, so every task leaves build and tests green and the risky UI work comes last on a tested foundation.

## Architecture Decisions
- **"Edited" is derived, not stored on the model:** `ExtractedField<T>.OriginalValue` is set once at mapping time; `IsEdited` = `Value` differs from `OriginalValue` (strings compared trimmed). Reverting clears it automatically; no flag can drift.
- **`CertDate` → `ExtractedField<DateOnly?>`**, the same fix already applied to decimals, so a date can be cleared. Done in its own early task because it ripples (service, mapper, razor, tests).
- **One shared validator** (`ExtractedValueValidator`) holds the rule table (type, range, required, max length) and is used by BOTH the UI (inline errors) and the store (defense in depth). Max lengths match the DB column sizes, which also prevents a SQL error on over-long text.
- **Persistence:** certificate columns = final values; evidence `ExtractedValueText` = what the model extracted; new `Edited` bit (default 0). One additive migration; applied to the Docker `CertDb` by Development auto-migrate. Existing P02V row stays valid.
- **UI risk reduced in two steps:** first refactor the three row renderers into one generic row (no behavior change, verified in the browser), then add editing on top.
- Each task keeps build at 0 warnings and tests green (scratch `--artifacts-path` builds so a running app never blocks).

## Dependency Graph
```
E1 model: OriginalValue/IsEdited + CertDate nullable ── E3 evidence Edited + migration + mapper ─┐
E2 validator (independent) ────────────────────────────────────────────────────────────────────┤
                                                                                                ├─ E4 store validation + edited round trip
E1 ── E5 generic row refactor (no behavior change) ── E6 editing UI (needs E2) ── E7 browser E2E + docs
```

## Task List
Tracked in tasks/todo-edit-values.md.

### Phase 1: Foundation
- [x] E1: `OriginalValue`/`IsEdited`; `CertDate` nullable; service sets originals; fix ripples
- [x] E2: `ExtractedValueValidator` (rule table) with tests
### Checkpoint: Foundation — build 0 warnings, tests green

### Phase 2: Persistence
- [x] E3: Evidence `Edited` + mapper rules + migration `AddEvidenceEdited` (script reviewed)
- [x] E4: Store re-validates; edited save round-trip tests
### Checkpoint: Persistence — tests green; migration applied to Docker DB on next Development start

### Phase 3: UI
- [x] E5: Generic review row (refactor only, browser-verified)
- [x] E6: Inline editing: edit/add/clear, validation, un-confirm, edited marker + revert, live missing-fields
### Checkpoint: UI — build clean; edit flow works in Chrome

### Phase 4: Ship
- [x] E7: Browser E2E incl. save of an edited cert, DB check, delete MY test row; docs

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| `CertDate` type change breaks several files/tests at once | Med | Do it alone in E1 with the compiler guiding; run full suite before moving on |
| Migration on the user's shared Docker SQL Server | Med | Additive `bit NOT NULL DEFAULT 0` only; review the script; applied only via Development auto-migrate; existing row verified afterwards |
| UI and store validation drift | Med | Single validator + rule table used by both; tests on the table |
| Blazor inline-edit state (per-row, keys, re-render, Enter/Esc) | Med | E5 refactor first; edit state keyed by (cert, field name) in a dictionary; verify keyboard in Chrome |
| Edited text longer than a DB column throws at save | Med | Validator enforces max lengths = column sizes (also guards model-returned text) |
| E2E leaves test data in the user's DB | Low | Use a clearly named test heat number; delete only that row (cascade removes evidence); verify counts; leave P02V alone |
| Browser E2E costs an extraction call | Low | One call; reuse the loaded review page for all edit scenarios |

## Open Questions
- None blocking (date format and range defaults accepted with the spec approval).
