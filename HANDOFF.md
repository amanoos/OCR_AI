# Hand-off — resume at the office (written 2026-11-26)

For a Claude session picking this project up on another machine. Read this first, then `CLAUDE.md` (the detailed project doc). This file is the "where are we and what's next" summary; it does not repeat the architecture.

## State in one paragraph

A Blazor Server (.NET 10) app extracts steel mill test report (MTR) data from PDFs with a vision LLM, shows it beside the PDF for human review and editing, and saves confirmed values to SQL Server with an audit trail. Everything built so far is implemented, unit-tested (401 tests) and verified end to end at home in Chrome against a real SQL Server and **direct Anthropic**. The **LiteLLM → Bedrock path has never run against the real proxy** — that is the reason for the office session. CI is green on `main`.

## Do this first at the office

1. **Get the code:** `git clone https://github.com/amanoos/OCR_AI.git` (the repo root is the project folder). Needs the **.NET 10 SDK**. Run `dotnet build` and `dotnet test tests/SteelCertExtraction.Tests` — expect 0 warnings, 401 passing.
2. **Nothing secret or machine-specific travels with the repo.** User-secrets are per machine and user; the home machine's `Llm:Provider` is `Anthropic`. Set them again here (never put keys in files or commit them; never paste them into chat):
   ```
   dotnet user-secrets set "Llm:Provider" "LiteLlm" --project SteelCertExtraction.csproj
   dotnet user-secrets set "Llm:LiteLlm:BaseUrl" "https://<proxy-host>/v1/" --project SteelCertExtraction.csproj
   dotnet user-secrets set "Llm:LiteLlm:ModelId" "<model id exactly as the proxy exposes it>" --project SteelCertExtraction.csproj
   dotnet user-secrets set "Llm:LiteLlm:ApiKey" "<proxy key>" --project SteelCertExtraction.csproj
   ```
   The user types or pastes the real values; use the same proxy settings as their experiment-tracker app.
3. **Connection check (no database or web server needed):**
   `dotnet run --project SteelCertExtraction.csproj -- --check-llm`
   Success is `Result : OK` and `Reply : OK` (this also proves images pass through the proxy). If it fails, read the `Error`/`Hint` lines. Known meanings: 404 = wrong BaseUrl path or model id; 401/403 = ApiKey; a "temperature" error with a non-400 status = set `Llm:LiteLlm:SendTemperature` to `false` (a 400 mentioning temperature is retried automatically without it); image BadRequest = model not vision-capable; cannot reach host = VPN/proxy; SSL error = corporate HTTPS interception.
4. **The sample certificate is not in the repo** (uploaded PDFs are git-ignored on purpose — they are vendor documents). The user must supply `Sample-MTR-304L-Stainless-Steel.pdf` (or any real cert) from their own files.
5. **Real extraction without DB or UI:**
   `dotnet run --project SteelCertExtraction.csproj -- --extract-file "<path to pdf>" [--show-raw]`
   Prints every parsed field (value, page, whether it has a box), the measurements, response size and elapsed time. Compare with the PDF. Known-good values for the 304L sample: heat P02V, grade 304/304L, carbon 0.014, nitrogen 0.074, cobalt 0.29, yield 34910 psi, tensile 85720 psi, hardness 163 HB, quantity 1.
6. **Full app:** `dotnet run --project SteelCertExtraction.csproj`. In Development it migrates the database at startup, so it needs `ConnectionStrings:CertDb` (a user-secret) pointing at a SQL Server reachable from the office with rights to create tables; the home Docker server is not reachable there. Use `tcp:<host>,<port>` style addresses if `localhost` misbehaves. Without it the app exits with a migration error (unrelated to the LLM; `--check-llm` and `--extract-file` still work).
7. **If anything fails, ask the user for:** the full `--check-llm` output (keys are masked), the app's error text, the model id used, and whether the temperature fallback kicked in.

## What is verified vs. not

| Verified (home, real) | Not verified |
|---|---|
| Direct Anthropic extraction of the 304L sample, original 20 fields + N, Co, hardness + measurements | **LiteLLM/Bedrock against the real proxy** (top priority) |
| Review UI: highlights, inline edit/add/clear/revert, validation, collapsed empty rows, measurements add/edit/remove | Impact/Charpy extraction on a real cert (only adding one by hand was tested) |
| Save to real SQL Server: typed columns, measurement rows, evidence with Edited/Confirmed flags, duplicate and invalid paths | ppm → % conversion for B/O/H/N |
| EF migrations (`InitialCreate`, `AddEvidenceEdited`, `AddMoreProperties`) applied to the home Docker `CertDb` | Carbon-steel certs, other vendors' layouts, multi-heat PDFs |

Only one sample PDF has ever been used, so accuracy on other layouts is unknown. Optional measurements vary run to run (melt practice, reduction ratio, mill name, gauge length, test direction). Highlights on inline running text are looser than on table cells.

## Suggested next work (after the proxy check passes)

1. Run 2–3 real certs through `--extract-file` — ideally one carbon-steel cert with Charpy impact values and ppm elements — and fix the prompt where it misreads. Prompt examples must NOT be copied from a test cert (a guard test enforces it; an earlier example leaked into the output).
2. Vendor key resolution: `CertUploadPage` passes `vendorKey: null`; it needs to come from the real PO/vendor record. Replace the placeholder vendor hints in `Services/CertExtractionPrompts.cs` with real vendor names and layouts.
3. Deferred, in rough priority: protected storage and auth for uploaded PDFs (they are publicly served from `wwwroot/uploaded-certs` today), a saved-certificates list/search page, editing a saved certificate, recording who confirmed/edited, concurrency tokens.
4. If this is merged into the experiment-tracker solution, reuse its LiteLLM `HttpClient` instead of keeping a second one.

## How work is done here (the user's workflow)

- For each feature: write a delta spec (`SPEC-<name>.md`) → plan (`tasks/plan-<name>.md`, `tasks/todo-<name>.md`) → **wait for the user's approval** → implement task by task with tests and real verification → update trackers/docs → checkpoint report. The user approves each step ("approved, go ahead with …"). Existing specs and plans show the format.
- **Branch rules:** `main` is protected, the `build-and-test` CI check is required, and administrators are enforced too. You **cannot push to `main` directly**: branch → commit → push → `gh pr create` → wait for green → merge (`gh pr merge --squash --delete-branch`). Merging is the user's call; ask before merging. Details in `CLAUDE.md`.
- Confirm before destructive or outward-facing actions. Only delete test data you created yourself; the saved P02V row in the home database is a real sample record and must stay.
- Never write secrets into files, commits or chat output.

## Gotchas learned the hard way

- **Blazor Server interactive island:** MudBlazor popover components (`MudTooltip`, `MudSelect`) crash here (no popover provider). Use plain `title` attributes and native `<select>`.
- **Request paths must stay relative** (no leading slash) in the LLM clients, or a base path like `/v1/` is dropped.
- **Direct Anthropic rejects `temperature`** for newer models; the Anthropic client never sends it.
- **EF Core 9+ throws on startup if the model has changes without a migration** — add the migration in the same change as any entity change.
- **`ExtractedField<T>`:** T must be a nullable value type (`decimal?`, `DateOnly?`) for missing values to parse.
- The numeric parser is lenient on purpose (`<0.001`, `ND` → null) so one odd trace value can't fail a whole extraction.
- Building while the app runs locks the DLLs; build into a scratch folder (`--artifacts-path`) or stop the app first.
