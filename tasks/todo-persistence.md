# Task List — EF Core Persistence (see tasks/plan-persistence.md, SPEC-persistence.md)

## P1: Packages, entities, DbContext — DONE (build 0 warnings; 29 existing tests still pass)
**Description:** Add EF Core SQL Server + Design packages; create `StoredCertificate`, `CertificateFieldEvidence`, `CertDbContext` with unique index on normalized heat number, decimal precision, string lengths, FK cascade.
**Acceptance criteria:**
- [ ] Packages added to the web csproj only (Sqlite is test-only, added in P3)
- [ ] Unique index on `HeatNumber`; nullable numeric columns (NULL = missing)
- [ ] Decimal precision: chemistry (9,4), mechanical (12,2)
**Verification:** `dotnet build` 0 warnings (use scratch `--artifacts-path` if the app is running and locking output)
**Dependencies:** None
**Files:** `SteelCertExtraction.csproj`, `Persistence/CertDbContext.cs`, `StoredCertificate.cs`, `CertificateFieldEvidence.cs`
**Scope:** Small

## P2: Initial migration — DONE (InitialCreate generated; SQL script reviewed; also added Persistence/CertDbContextFactory.cs for design-time tooling)
**Description:** Ensure `dotnet-ef` is available; generate `InitialCreate` into `Persistence/Migrations`; generate the SQL script and review it.
**Acceptance criteria:**
- [ ] Migration files committed under `Persistence/Migrations`
- [ ] Script shows unique index on HeatNumber, `decimal(9,4)`/`decimal(12,2)` columns, FK with cascade, nullable numerics
**Verification:** `dotnet ef migrations script` output reviewed; build clean
**Dependencies:** P1
**Files:** `Persistence/Migrations/*`
**Scope:** Small

### Checkpoint: After P1–P2 — build clean, schema reviewed, review with human

## P3: Mapper + tests — DONE (11 new tests; 40 total pass, 0 warnings)
**Description:** `CertificateMapper` maps `SteelCertificate` + `ExtractionResult` metadata + stored PDF path → `StoredCertificate` with evidence rows for every non-null field. Add `Microsoft.EntityFrameworkCore.Sqlite` to the test project.
**Acceptance criteria:**
- [ ] Null numeric values → NULL columns, never 0; no evidence row for a field with null value
- [ ] Heat number normalized (trim + upper); original text preserved in evidence
- [ ] Evidence carries page, bbox, confirmed flag
**Verification:** mapper unit tests pass
**Dependencies:** P1
**Files:** `Persistence/CertificateMapper.cs`, `tests/.../CertificateMapperTests.cs`, test csproj
**Scope:** Small–Medium

## P4: `CertificateStore` + tests — DONE (10 new tests; 50 total pass, 0 warnings)
**Description:** `ICertificateStore.SaveConfirmedAsync` validates (Heat Number + Grade present and confirmed), checks duplicate, saves; maps unique-constraint violations to `Duplicate`.
**Acceptance criteria:**
- [ ] Missing/unconfirmed HeatNumber or Grade → `Invalid` with reasons, nothing saved
- [ ] Existing heat number (case/whitespace-insensitive) → `Duplicate(existing summary)`, no new row
- [ ] Race: constraint violation on insert → `Duplicate`, not an exception
- [ ] Valid cert → `Saved(id)`; row + evidence persisted; unconfirmed optional values stored with `Confirmed=false`
**Verification:** store tests (SQLite in-memory) pass
**Dependencies:** P3
**Files:** `Persistence/ICertificateStore.cs`, `CertificateStore.cs`, `tests/.../CertificateStoreTests.cs`
**Scope:** Medium

### Checkpoint: After P3–P4 — all tests green

## P5: DI, config, Development auto-migrate — DONE (54 tests pass, 0 warnings; startup verified: missing secret fails fast naming the key; unreachable server logs a clear migration error. Migration against the real Docker SQL Server is NOT yet verified — needs the user's secret)
**Description:** `AddCertPersistence(config)` registers `IDbContextFactory<CertDbContext>` (SQL Server) and the store; fails fast if `ConnectionStrings:CertDb` missing; in Development, apply migrations at startup. Placeholder connection string in appsettings (no password).
**Acceptance criteria:**
- [ ] Missing connection string → startup error naming `ConnectionStrings:CertDb`
- [ ] Auto-migrate only when `IsDevelopment()`
- [ ] appsettings has placeholder only; docs say to use user-secrets
**Verification:** DI test for missing/valid config; `dotnet build`; app starts with user-provided secret (manual)
**Dependencies:** P4
**Files:** `Persistence/PersistenceServiceCollectionExtensions.cs`, `Program.cs`, `appsettings.json`, `tests/.../PersistenceRegistrationTests.cs`
**Scope:** Medium

## P6: Wire Confirm & Save in the UI — CODE DONE (compiles, 54 tests pass); UI behavior NOT yet verified in a browser — needs the user's DB secret (manual check in P7)
**Description:** `CertUploadPage.HandleConfirm` calls the store with the cert and `_result` metadata + saved PDF URL; `CertReviewPage` shows per-cert outcome (Saved / Duplicate with existing record / Invalid reasons) and disables re-save after success.
**Acceptance criteria:**
- [ ] Success → "Saved" state, button disabled
- [ ] Duplicate → message with existing heat number, grade, source file, confirmed time
- [ ] Invalid → lists what must be confirmed
- [ ] Unexpected errors logged and shown generically
**Verification:** `dotnet build`; manual: save sample cert; save again → duplicate message; try without confirming heat number → blocked
**Dependencies:** P5
**Files:** `Components/CertUploadPage.razor`, `Components/CertReviewPage.razor`, (maybe `Components/Layout/MainLayout.razor`)
**Scope:** Medium

### Checkpoint: After P5–P6 — app runs, save works against Docker SQL Server (manual)

## P7: Docs + manual end-to-end — DONE (docs updated; manual E2E run in Chrome on 2026-11-26: Invalid, Duplicate and Saved all verified, DB rows checked)
**Description:** Update CLAUDE.md (status, config, commands, remaining gaps); mark spec implemented; record manual E2E result.
**Acceptance criteria:**
- [ ] CLAUDE.md persistence section + gap #1 closed; SPEC-persistence.md `Status: implemented`
- [ ] Manual E2E recorded (pass/fail)
**Verification:** build + tests green
**Dependencies:** P6
**Files:** `CLAUDE.md`, `SPEC-persistence.md`
**Scope:** Small

### Checkpoint: Complete — all SPEC-persistence success criteria met
