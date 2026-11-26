# Implementation Plan: EF Core Persistence for Confirmed Certificates

Source spec: SPEC-persistence.md (approved 2026-11-26)
Task list: tasks/todo-persistence.md (separate from tasks/plan.md / todo.md, which still have an open item: live LiteLLM smoke test)

## Overview
Persist a user-confirmed `SteelCertificate` (plus provenance) to SQL Server through a store that enforces "Heat Number + Grade present and confirmed" and rejects duplicate heat numbers. Wire the existing "Confirm & Save" button to it and show the outcome.

## Architecture Decisions
- Separate persistence entities (`StoredCertificate`, `CertificateFieldEvidence`); explicit mapper from the extraction models.
- `ICertificateStore` over `IDbContextFactory<CertDbContext>` (Blazor Server). Returns `Saved | Duplicate | Invalid` — no exceptions for expected outcomes.
- Heat number normalized (trim + upper) into a unique-indexed column; the DB constraint is the final guard against races.
- **Migration is verified without a live DB** by generating the SQL script (`dotnet ef migrations script`); the user's Docker SQL Server is exercised by auto-migrate-in-Development at app start. The connection-string secret is set by the user via user-secrets — never pasted into chat or committed.
- Tests use SQLite in-memory (enforces the unique index). SQL Server-specific behavior (decimal precision) is verified manually.

## Dependency Graph
```
P1 packages + entities + DbContext ── P2 initial migration
        └── P3 mapper (+tests) ── P4 store (+tests) ── P5 DI/config/dev auto-migrate ── P6 UI wiring ── P7 docs + manual E2E
```

## Task List
Tracked in tasks/todo-persistence.md.

### Phase 1: Foundation
- [x] P1: Packages, entities, `CertDbContext`
- [x] P2: Initial migration (+ SQL script check)
### Checkpoint: Foundation — build 0 warnings; migration script reviewed

### Phase 2: Core logic
- [x] P3: Mapper with tests
- [x] P4: `CertificateStore` with tests
### Checkpoint: Core — all tests green (incl. duplicate + required-confirmed rules)

### Phase 3: Integration
- [x] P5: DI registration, connection string config, Development auto-migrate
- [x] P6 (code; browser check pending): Wire Confirm & Save in the UI
### Checkpoint: Integration — app starts; save works against SQL Server (manual)

### Phase 4: Ship
- [x] P7: Docs; manual end-to-end with the sample cert

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| `dotnet-ef` tool not installed / version mismatch with net10 | Med | Check early in P2; install/update the tool (a global tool install is outside the repo — will say so) |
| Connection-string secret needed to test against Docker SQL Server; I must not see the password | Med | User sets `ConnectionStrings:CertDb` via user-secrets; auto-migrate runs when they start the app; I verify via SQL script + SQLite tests |
| SQLite ≠ SQL Server (decimal precision, collations) | Med | Normalize heat number in code (upper+trim) so uniqueness doesn't depend on collation; manual check on SQL Server |
| Review-page UI state (saved/duplicate message per card) adds Razor complexity | Low | Keep it to a small per-cert result map passed as a parameter |
| `MudSnackbarProvider` may be missing from layout | Low | Check MainLayout in P6; use inline `MudAlert` if so |
| Many files already modified this session | Low | Keep each task ≤5 files; checkpoint reviews |

## Open Questions
- Docker SQL Server port/login: assumed `localhost,1433` with `TrustServerCertificate=True`; user supplies the real connection string as a secret.
