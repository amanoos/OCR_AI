# Spec Delta: EF Core Persistence for Confirmed Certificates

Status: implemented and verified (2026-11-26) — migration applied to the Docker SQL Server; Invalid / Duplicate / Saved paths exercised in the browser and rows checked in the database — SQL Server in Docker; new `CertExtraction` database; heat number unique globally.
Deviation from this spec: `ConnectionStrings:CertDb` is empty in appsettings.json (instead of a LocalDB placeholder) so a missing secret fails fast with a clear message.

## Objective
"Confirm & Save" on the review screen currently only logs. Persist a user-confirmed certificate to SQL Server so confirmed mill-test-report data is stored with its source PDF and per-field provenance (page, bounding box, confirmed flag). Nothing is saved unless a human confirmed the required fields.

Implements CLAUDE.md known gap #1. Inherits everything else from SPEC.md.

## Current Behavior
- `CertReviewPage` → `OnConfirm` → `CertUploadPage.HandleConfirm` only logs the heat number.
- Extraction models (`SteelCertificate`, `ExtractedField<T>`) are plain C# models, not EF entities.
- Uploaded PDFs are saved to `wwwroot/uploaded-certs` and referenced only by URL in the page state.

## New Behavior
- Clicking **Confirm & Save** on a cert card saves that certificate (one DB row per heat number; a multi-heat PDF is saved one card at a time).
- Save is **blocked** unless `HeatNumber` and `Grade` are present **and confirmed**. Other extracted values are saved with their own `Confirmed` flag as-is (unconfirmed values stored but marked).
- If a certificate with the same heat number already exists, the save is **rejected** and the UI shows the existing record (heat number, grade, source file, confirmed time). No duplicate is created.
- Saved record keeps: typed values, source file name and stored PDF path/URL, extraction metadata (document id, model used, extracted-at), confirmed-at (UTC), and per-field provenance.
- Success and failure are shown to the user (MudBlazor snackbar/alert); the card shows a "Saved" state and can't be saved twice.

## Design
**Database:** SQL Server via `Microsoft.EntityFrameworkCore.SqlServer`; connection string `ConnectionStrings:CertDb` (placeholder LocalDB in appsettings, real value via user-secrets). Separate database/schema `cert`, not shared with the experiment tracker unless told otherwise.

**Separate persistence entities** (extraction/UI models unchanged, mapped explicitly):
```
StoredCertificate  (Id, HeatNumber [unique, normalized trim+upper], Grade, Specification,
                    CertifyingLab, CertDate, PurchaseOrderNumber,
                    chemistry decimal(9,4)? columns, mechanical decimal(12,2)? columns,
                    ReductionOfArea string?, SourceFileName, StoredPdfPath,
                    ExtractionDocumentId, ModelUsed, ExtractedAtUtc, ConfirmedAtUtc)
CertificateFieldEvidence (Id, CertificateId FK, FieldName, ExtractedValueText?, Page, BboxX/Y/W/H?, Confirmed)
```
**Access:** `ICertificateStore.SaveConfirmedAsync(...)` → result `Saved(id)` | `Duplicate(existing)` | `Invalid(reasons)`; implemented over `IDbContextFactory<CertDbContext>` (Blazor Server — no long-lived DbContext). Unique index is the final guard; a duplicate caught by the constraint (race) maps to `Duplicate`, not an exception.

**Migrations:** initial migration committed. Auto-apply on startup in Development only; elsewhere run `dotnet ef database update` manually.

## Commands
```
Add package:  dotnet add SteelCertExtraction.csproj package Microsoft.EntityFrameworkCore.SqlServer
              dotnet add SteelCertExtraction.csproj package Microsoft.EntityFrameworkCore.Design
Tool:         dotnet tool install --global dotnet-ef   (if not already installed)
Migration:    dotnet ef migrations add InitialCreate --project SteelCertExtraction.csproj --output-dir Persistence/Migrations
Apply:        dotnet ef database update --project SteelCertExtraction.csproj
Secret:       dotnet user-secrets set "ConnectionStrings:CertDb" "<conn string>" --project SteelCertExtraction.csproj
Test:         dotnet test tests/SteelCertExtraction.Tests
```

## Project Structure
```
Persistence/CertDbContext.cs, StoredCertificate.cs, CertificateFieldEvidence.cs, Migrations/
Persistence/ICertificateStore.cs, CertificateStore.cs, CertificateMapper.cs
tests/SteelCertExtraction.Tests/Persistence*Tests.cs
```

## Success Criteria
1. Confirming a cert with confirmed Heat Number + Grade creates one `StoredCertificate` with correct values and one evidence row per non-null extracted field.
2. Missing/unconfirmed Heat Number or Grade → nothing saved; UI states why.
3. Same heat number again → no new row; UI shows the existing record.
4. Missing (null) numeric values are stored as NULL, never 0.
5. Unconfirmed optional values are stored with `Confirmed = false`.
6. Initial migration applies cleanly to a fresh SQL Server (LocalDB) database (manual check) and the app saves a real extraction end to end (manual check).
7. `dotnet build` 0 warnings; all tests pass.

## Testing Strategy
EF Core with SQLite in-memory (enforces the unique index, unlike the InMemory provider) via `Microsoft.EntityFrameworkCore.Sqlite` in the **test project only**. Cover: mapper (incl. nulls, evidence rows), store (save, invalid, duplicate, constraint-race duplicate), and decimal precision round-trip as far as SQLite allows. SQL Server specifics (precision, migration) are verified manually.

## Boundaries
- Always: validate required+confirmed in the store (not only the UI); keep mapping explicit; use `IDbContextFactory`; parameterized EF only.
- Ask first: adding packages beyond those listed; schema changes after the initial migration; sharing the experiment tracker's database.
- Never: commit connection strings with credentials; auto-migrate outside Development; store a value of 0 for a missing number; save on a failed required-field check.

## Out of Scope (Deferred)
- Page to list/view/search saved certificates.
- Editing extracted values before save (UI is confirm-only today).
- Delete/re-extract/versioning of a saved certificate.
- Recording *who* confirmed (no authentication exists yet).
- Linking to PO/vendor master data; real vendor key resolution.
- Moving PDFs to protected storage (still `wwwroot/uploaded-certs`, publicly served — known gap).
- Concurrency tokens/optimistic locking.

## Open Questions
1. Do you have SQL Server or LocalDB available on this machine, and is a new database `CertExtraction` acceptable (vs. adding tables to the experiment tracker's DB)?
2. Heat number uniqueness is global across vendors — OK, or should it be unique per (vendor, heat number)? (No vendor concept exists in the app yet; assumed global.)
