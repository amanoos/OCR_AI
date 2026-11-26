# Steel Vendor Cert Extraction — Project Handoff

> Resuming on another machine (e.g. the office)? Start with [HANDOFF.md](HANDOFF.md): current state, what to do first, what is unverified, next work.

## What this is

A feature for extracting structured data (heat number, grade, chemistry,
mechanical properties) from steel vendor certification PDFs (mill test
reports), using a vision-LLM (Claude via the user's corporate LiteLLM →
AWS Bedrock proxy) instead of traditional OCR. Extracted data is shown
side-by-side with the source PDF for human verification before saving —
nothing is auto-trusted into the database.

## Why this approach (context for future decisions)

- **Vision-LLM over traditional OCR / Textract / Document Intelligence**:
  Only 1-3 vendors, low volume (a few PDFs/week), stable formats per
  vendor. Template-training platforms (Azure Doc Intelligence custom
  models, Textract adapters) are overkill at this scale — prompt-level
  vendor hints are sufficient and far less maintenance.
- **Best-effort extraction + human review, not auto-insert**: Compliance-
  sensitive data (steel cert traceability). The UI must let a user confirm
  correctness against the source PDF before anything is saved.
- **Bounding boxes for source highlighting, region-level accuracy is
  enough** (confirmed with user — not pixel-precise). The model is asked
  to return normalized (0–1 fraction of page) bounding boxes directly,
  rather than running a second OCR pass to locate text. This is the
  cheapest approach that meets the stated accuracy bar; revisit only if
  testing shows the model's boxes are too unreliable.
- **PDF.js for viewing** (user's choice, already decided) — a Blazor
  component renders pages to canvas and overlays a highlight `<div>`
  positioned via JS interop using the bbox fractions.

## Architecture / data flow

```
CertUploadPage.razor (upload UI)
  → saves PDF to disk, gets stream
  → ICertExtractionService.ExtractAsync(stream, fileName, vendorKey)
      → IPdfRasterizer.RasterizePagesAsync (PDF → PNG per page)
      → ILlmChatClient.SendMultimodalAsync (system+user prompt + page images → LiteLLM → Bedrock/Claude)
      → parses JSON response → maps to SteelCertificate domain model
  → CertReviewPage.razor (review UI)
      → PDF.js viewer (left) + extracted fields with confirm checkboxes (right)
      → hover/click a field → highlightRegion() positions overlay div via bbox
      → "Confirm & Save" → OnConfirm → CertUploadPage.HandleConfirm
          → ICertificateStore.SaveConfirmedAsync (validates Heat Number + Grade present AND confirmed,
            rejects duplicate heat numbers, writes StoredCertificate + per-field evidence to SQL Server)
          → outcome (Saved / Duplicate / Invalid) shown on that cert's card
```

## File-by-file status

| File | Status |
|---|---|
| `Models/CertExtractionModels.cs` | ✅ Complete — `SteelCertificate`, `ExtractedField<T>`, `BoundingBox`, etc. |
| `Services/CertExtractionPrompts.cs` | ✅ Complete — system prompt + schema. Has placeholder vendor hints for `"vendor-a"`/`"vendor-b"` keys — **replace with real vendor names and real layout descriptions once known.** |
| `Services/CertExtractionService.cs` | ✅ Complete — orchestration, JSON parsing, mapping to domain model. |
| `Services/LiteLlmChatClient.cs` | ⚠️ Functional but UNVERIFIED against the user's actual LiteLLM proxy. Assumes OpenAI-compatible `chat/completions` endpoint (relative path — a leading slash previously dropped the `/v1/` base path; fixed) and `image_url` data-URI content parts. **Must smoke-test against the real proxy** — endpoint path and image-passing convention may differ from what the experiment tracker's existing LiteLLM client already does. Sends `temperature: 0` by default; if the proxy answers **400** mentioning `temperature` (as direct Anthropic did) it retries once without it and stops sending it for that client; `Llm:LiteLlm:SendTemperature=false` omits it outright. Verify with `--check-llm` (see "Office test checklist"). |
| `Services/ILlmChatClient.cs`, `Services/LlmOptions.cs` | ✅ Done — provider-agnostic interface + `Llm:*` options (see "LLM providers" below). |
| `Services/AnthropicChatClient.cs` | ✅ **Verified live** (2026-11-19): a real cert PDF extracted end to end via direct Anthropic (`v1/messages`, `x-api-key`, `anthropic-version`, base64 image blocks). Does NOT send `temperature` — newer Claude models reject it with a 400 (`temperature is deprecated for this model`). |
| `Services/ServiceCollectionExtensions.cs` | ✅ Done — `AddCertExtraction` picks the provider from `Llm:Provider`, validates config at startup (fails fast naming the missing key), registers only the active provider's HttpClient/client, rasterizer, extraction service. Legacy `LiteLlm:*` keys still work as a fallback (logs a warning). Config values in `appsettings.json` are **placeholders**. |
| `Services/LlmConnectionCheck.cs` | ✅ `dotnet run --project SteelCertExtraction.csproj -- --check-llm`: one tiny request (text + 1×1 PNG) through the configured provider; prints provider/URL/model/outcome/latency/reply or error + hint; masks API keys; exit 0/1. Needs no DB or web server. Verified live against direct Anthropic (2026-11-26). |
| `wwwroot/lib/pdfjs/` | ✅ Vendored PDF.js 3.11.174 (`pdf.min.js`, `pdf.worker.min.js`), SRI-verified against cdnjs; see `NOTICE.txt` there. **Upgrade both files together** (worker must match the library). |
| `tests/SteelCertExtraction.Tests/` | ✅ 174 xUnit tests: both LLM clients (fake `HttpMessageHandler`), LiteLLM temperature fallback, `--check-llm`, provider selection, extraction-service JSON parsing/null handling, `ExtractedField` edited derivation, `ExtractedValueValidator`, and persistence (mapper incl. edited/added/cleared evidence, store on SQLite in-memory incl. value-rule and edited round-trip tests, DI). `dotnet test tests/SteelCertExtraction.Tests`. The web csproj excludes `tests\**` from its globs. |
| `Program.cs`, `Components/App.razor`, `Components/Pages/Certs.razor` | ✅ Done — Blazor Server (net10.0) scaffold, MudBlazor, 32MB SignalR limit, `UseStaticFiles` for runtime uploads, PDF.js 3.11.174 self-hosted under `wwwroot/lib/pdfjs` (the app makes **no requests to external hosts**; no web-font link, system font fallback), `--check-llm` CLI mode handled at the top of `Program.cs`, routable page at `/`. |
| `Services/PdfToImageRasterizer.cs` | ✅ Implemented against PDFtoImage v4+ API (verified current via web search at handoff time — `RenderOptions(Dpi:)`, `ToImages` for multi-page). NuGet packages needed: `PDFtoImage`, `SkiaSharp`. |
| `Components/CertUploadPage.razor` | ✅ Functional. `HandleConfirm` calls `ICertificateStore` and tracks per-cert outcomes. File save path is local disk under `wwwroot/uploaded-certs` — **placeholder**, replace with whatever storage convention/compliance requirement applies to vendor docs in this org. `vendorKey` resolution is hardcoded `null` — needs wiring to actual PO/vendor lookup. After a save there is no "upload another" action (reload the page). |
| `Components/CertReviewPage.razor` | ✅ Functional. Shows **every field the extraction returns** (heat/grade/spec/lab/date/PO, all 10 chemistry elements, yield/tensile/elongation/reduction of area) each with a confirm checkbox when it has a value, and **every row is editable inline** (see "Edit before saving"); verified in the browser 2026-11-26. Uses plain `title` tooltips — `MudTooltip` crashes inside this interactive-server island (needs a popover provider the static layout doesn't expose to it). Takes a `SaveResults` parameter and shows Saved / Duplicate / Invalid alerts per card; button disabled while saving and after save. Save UI verified in the browser (red Not saved list, yellow duplicate message, green Saved + disabled "SAVED" button). |
| `wwwroot/js/pdf-cert-viewer.js` | ✅ Complete. Requires global `pdfjsLib` — loaded from the vendored `wwwroot/lib/pdfjs` in `App.razor`. |
| `wwwroot/css/cert-review.css` | ✅ Complete. |
| `Persistence/*` (see "Persistence" below) | ✅ Code complete, unit-tested (SQLite in-memory) and **verified end to end against the real Docker SQL Server (2026-11-26)**: Invalid, Duplicate and Saved paths all exercised in the browser; DB rows checked (NULL for missing vanadium, 19 evidence rows with bboxes, only ticked fields marked confirmed). |
| Unit tests | ⚠️ 401 tests (as of the more-properties work; also covers measurements, lenient parsing, `--extract-file` report, prompt contract guards): LLM clients/provider selection/temperature fallback/`--check-llm`, extraction-service parsing, validator, edited-value model, persistence mapper/store/DI. Prompts, rasterizer and the Razor UI have no tests. |

## LLM providers (SPEC.md, tasks/plan.md)

One provider is active per deployment, chosen by `Llm:Provider`:

| Provider | Use | Config |
|---|---|---|
| `LiteLlm` (default) | Work: corporate LiteLLM proxy → Bedrock (or any model the proxy exposes) | `Llm:LiteLlm:BaseUrl` (ends in `/v1/`), `ApiKey` (optional), `ModelId` |
| `Anthropic` | Personal/dev: direct Anthropic Messages API | `Llm:Anthropic:ApiKey` (required), `ModelId` (default `claude-sonnet-5-5`), `BaseUrl`, `ApiVersion` |

Never commit keys: `dotnet user-secrets set "Llm:Anthropic:ApiKey" "<key>" --project SteelCertExtraction.csproj`, or env var `Llm__Anthropic__ApiKey`. Request paths must stay **relative** (no leading slash) so base paths like `/v1/` are preserved.

Extra LiteLLM options: `Llm:LiteLlm:SendTemperature` (default `true`; `false` never sends `temperature`). Independently, a 400 mentioning `temperature` triggers one retry without it. The direct-Anthropic client never sends `temperature`. (SPEC-office-readiness.md, tasks/plan-office-readiness.md)

## Office test checklist: LiteLLM/Bedrock

The LiteLLM/Bedrock path has only been tested with fake HTTP handlers — **not against the real proxy**. Do this from the office network (the project folder isn't a git repo; copy it over, with .NET 10 SDK installed). **User-secrets are per machine/user — nothing set at home travels with the folder**, and the secret `Llm:Provider` on the home machine currently says `Anthropic`.

1. Set the secrets (from the project folder; same proxy as the experiment tracker):
   ```
   dotnet user-secrets set "Llm:Provider" "LiteLlm" --project SteelCertExtraction.csproj
   dotnet user-secrets set "Llm:LiteLlm:BaseUrl" "https://<proxy-host>/v1/" --project SteelCertExtraction.csproj   # must end with /v1/ (or whatever the proxy's OpenAI-compatible root is)
   dotnet user-secrets set "Llm:LiteLlm:ModelId" "<model id exactly as the proxy exposes it>" --project SteelCertExtraction.csproj
   dotnet user-secrets set "Llm:LiteLlm:ApiKey" "<proxy key>" --project SteelCertExtraction.csproj
   ```
2. Run the connection check (no database needed): `dotnet run --project SteelCertExtraction.csproj -- --check-llm`. Success = `Result : OK` and `Reply : OK` (this also proves images pass through the proxy). If `FAILED`, read the `Error` and `Hint` lines:
   - 404 → wrong `BaseUrl` (path) or model id · 401/403 → `ApiKey` · "temperature" error with a non-400 status → set `Llm:LiteLlm:SendTemperature` to `false` · image/BadRequest → model isn't vision-capable · cannot reach host → VPN/network/proxy · certificate/SSL → corporate HTTPS interception (machine must trust its cert).
3. Run the real app: `dotnet run --project SteelCertExtraction.csproj`. In Development it migrates the database at startup, so it needs `ConnectionStrings:CertDb` pointing at a SQL Server reachable from the office (the home Docker SQL Server won't be) with rights to create tables — or the app will exit with a migration error (that is unrelated to the LLM; `--check-llm` still works without it).
3b. Optional quick check without DB/UI: `dotnet run --project SteelCertExtraction.csproj -- --extract-file <path to a cert pdf>` prints every parsed field and measurement (use `--show-raw` for the model's JSON). Handy for trying new certs (esp. a carbon-steel cert with Charpy impact values and ppm elements, which are still unverified).
4. Upload the sample cert; compare extracted values with the PDF; hover fields to check highlights; confirm Heat Number + Grade and save.
5. If anything fails, bring back: the full `--check-llm` output (keys are masked), the app's error text, the model id used, and whether the temperature fallback was needed.

## Persistence (SPEC-persistence.md, tasks/plan-persistence.md)

Confirmed certificates are saved to SQL Server (database `CertExtraction`, Docker). Separate persistence entities — the extraction/UI models are unchanged and mapped explicitly.

- **Entities** (`Persistence/`): `StoredCertificate` (typed nullable columns — NULL means "not on the cert", never 0; heat number stored normalized trim+UPPER with a **unique index**), `CertificateFieldEvidence` (per field: the text **the model extracted**, page, bbox, `Confirmed`, and `Edited` — see "Edit before saving"). `CertDbContext`, `CertificateMapper`, `ICertificateStore`/`CertificateStore`.
- **Save rule (enforced in the store, not just the UI):** Heat Number AND Grade must be present and confirmed → else `Invalid` with reasons, nothing written. Same normalized heat number already saved → `Duplicate` (existing record shown), nothing written; a unique-index race is also mapped to `Duplicate`. Other optional values are saved with their own `Confirmed` flag.
- **Config:** `ConnectionStrings:CertDb` is **intentionally empty in appsettings.json** and the app fails fast at startup without it. Set it per machine:
  `dotnet user-secrets set "ConnectionStrings:CertDb" "Data Source=tcp:127.0.0.1,1433;Initial Catalog=CertDb;User Id=<user>;Password=<password>;TrustServerCertificate=Yes" --project SteelCertExtraction.csproj` (or env var `ConnectionStrings__CertDb`). Never commit it.
  - **Local setup actually used (2026-11-26):** database `CertDb` on the Docker SQL Server container `todo-app-db-1` (published only on `127.0.0.1:1433`). Use `tcp:127.0.0.1,1433`, **not** `localhost` — `localhost` failed (IPv4-only port binding; SqlClient fell back to named pipes). The app login needs rights on the database (the one-time `CREATE DATABASE` + `db_owner` grant was done by an admin; a normal app login lacks `CREATE DATABASE`). All three migrations (`InitialCreate`, `AddEvidenceEdited`, `AddMoreProperties`) have been applied to `CertDb`.
- **Migrations:** `Persistence/Migrations` (`InitialCreate`). Applied automatically at startup **in Development only** (`MigrateCertDatabaseAsync`); elsewhere run `dotnet ef database update --project SteelCertExtraction.csproj` deliberately. `Persistence/CertDbContextFactory.cs` exists only for `dotnet ef` tooling (reads optional `CERTDB_CONNECTION`). Add a migration: `dotnet ef migrations add <Name> --project SteelCertExtraction.csproj --output-dir Persistence/Migrations`. (Global `dotnet-ef` is 10.0.11 vs runtime 10.0.15 — only a warning.)
- **Not built (deferred):** list/view/search saved certs, editing a *saved* certificate, delete/versioning, recording *who* confirmed (no auth), PO/vendor linking, protected PDF storage, concurrency tokens. SQL Server specifics (decimal precision) are only manually verifiable.

## Edit before saving (SPEC-edit-values.md, tasks/plan-edit-values.md)

Reviewers can correct, add or clear **any** field on the review screen before saving; nothing an edit touches is trusted until confirmed again. Verified end to end in Chrome against the real Docker SQL Server (2026-11-26).

- **UX:** every row has a pencil (or "add" when empty) → inline input, **Enter** applies, **Esc** cancels. An edit **un-ticks the confirm box** (must be ticked again — never auto-confirmed). Edited rows show an orange **EDITED** badge (tooltip = what the model extracted; "nothing" for an added value) and a revert arrow. Hover highlight keeps the original bounding box; added values have none. The "Missing: …" header warning is computed live. Rows are read-only while saving and after a successful save; "Confirm & Save" is disabled while an editor is open.
- **Model:** `ExtractedField<T>.OriginalValue` (what the model said, set once in `CertExtractionService`) + derived `IsEdited` (`Value != OriginalValue`; text compared trimmed, null == blank; reverting clears it automatically). `CertDate` is `ExtractedField<DateOnly?>` so a date can be cleared.
- **Validation — one shared rule table, `Services/ExtractedValueValidator.cs`**, used by BOTH the review screen (inline errors) and `CertificateStore` (re-checked on save): chemistry % and elongation 0–100; yield/tensile > 0 and ≤ 1,000,000 psi; numbers strictly invariant (`.` decimal; no `,`/exponent/`<0.01`); dates `YYYY-MM-DD`, 1950-01-01 … today (+1 day for time zones); Heat Number and Grade required (can't be cleared); text max lengths equal the DB column sizes (so over-long text can't cause a SQL error). A test fails if a field is added to the extraction model without a rule, and another if a text rule's length drifts from the EF column size. **Behavior change:** a model value that violates a rule (e.g. a chemistry % > 100) now blocks the save until corrected or cleared.
- **Persistence:** certificate columns hold the **final** values; `CertificateFieldEvidence.ExtractedValueText` is always **what the model extracted** (NULL if nothing) and the new `Edited` bit (migration `AddEvidenceEdited`, `NOT NULL DEFAULT 0`) says whether the reviewer changed it. A cleared value → column NULL + evidence row (original text, `Edited=1`); an added value → `ExtractedValueText` NULL, `Edited=1`, page 0, no box; untouched empty fields → no row. `Confirmed` is per field as the reviewer left it.
- **Verified in the DB (E7, test heat `E7-TEST-EDIT`, since deleted — only that row):** final values correct; evidence originals/`Edited`/`Confirmed` exactly as above; 20 evidence rows, 5 edited, 3 confirmed; the pre-existing P02V row unchanged (19/0/2).
- **Not covered in the browser:** editing a heat number onto an existing one → duplicate (covered by `CertificateStoreEditedTests`, unit level only).
- **Deferred:** editing a saved certificate, per-edit history (only original vs final is kept), bulk edit/undo stacks, recording who edited. (N/Co/hardness and other properties were added afterwards — see "More steel properties".)

## More steel properties (SPEC-more-properties.md, tasks/plan-more-properties.md)

Beyond the original 20 fields the extraction now captures **16 more chemistry elements + hardness as typed fields** and a flexible **"other measurements" list**. Verified end to end on the 304L sample in Chrome + the real Docker SQL Server (2026-11-26).

- **Typed fields** (nullable columns, `ExtractedField<decimal?>`): Nitrogen, Cobalt, Boron, Aluminum, Titanium, Niobium, Tungsten, Tin, Lead, Arsenic, Antimony, Calcium, Oxygen, Hydrogen, Tantalum, CarbonEquivalent (weight %, 0–100) and `HardnessValue` (>0, ≤2000) + `HardnessScale` (text ≤20, e.g. HB). Included **only if the cert prints them** (the prompt says omit, never guess; the original 20 keep "always include key"). ppm is converted to % by the model (÷10,000); `<0.001`/ND → null; "CO" means cobalt; hardness = first scale listed. Rules live in the shared `ExtractedValueValidator` table.
- **Measurements** (`Models/Measurements.cs`): `MeasurementCatalog` has 22 kinds in 4 categories (Impact 5, Tensile details 4, Heat treatment 7, Product info 6) plus `other` with the printed label (max 10 per cert, first listed wins, label ≤100). A measurement = key + label + category + `ExtractedField<MeasurementValue(Value, Unit, Condition)>`, shown as `value unit (condition)`, **one value per property, exactly as printed**. Parsed from a raw `JsonElement` so one malformed entry never fails the response. Limits: value 200, unit 20, condition 200 (`MeasurementValidator`, equal to the DB columns).
- **Persistence:** migration `AddMoreProperties` (applied to `CertDb`): 18 new nullable columns on `StoredCertificates`, new table `CertificateMeasurements` (unique CertificateId+Key+Label, cascade delete) and `CertificateFieldEvidence.FieldName` widened to 150. Evidence for measurements is `Measurement:<key>` (or `Measurement:other:<label>`), `ExtractedValueText` = what the model extracted. Mapper rule: a measurement row is written when it has a value; a **removed** measurement writes evidence (original text, `Edited=1`) but no row; an added one has `ExtractedValueText` NULL, `Edited=1`. Missing numbers are NULL, never 0.
- **Review screen:** rows are generated from descriptors per section; empty optional rows collapse into "N not found on this certificate — show" (Heat/Grade always visible). The PDF pane is sticky so highlights stay visible while the long list scrolls (disabled ≤900px). "Other measurements" section: edit (value/unit/condition), remove (shows "removed" + revert), revert, edited badge, and "Add measurement" (native `<select>` of catalog kinds not yet listed + "Other — type a label…"). The pure rules are in `Services/MeasurementEditor.cs`. Plain `title` tooltips and a native `<select>` again because MudBlazor popover components crash in the interactive island.
- **Lenient parsing:** `LenientDecimalConverter` (a non-number such as "<0.001"/"ND" → null instead of failing the whole extraction, for ALL numeric fields) and `LenientStringConverter` (numbers → text).
- **Prompt lesson:** examples in the prompt must NOT come from the test cert — a "6 pieces" example leaked and made the model read quantity from the Mark column. Quantity may only come from a Pieces/Pcs/Qty/Quantity column; a guard test (`PromptContractTests`) blocks sample-derived examples and checks every catalog key is offered.
- **Prompt tuning tool:** `dotnet run --project SteelCertExtraction.csproj -- --extract-file <pdf> [--show-raw]` runs the real extraction and prints every parsed field (value, page, whether it has a box) + the measurements, response size and elapsed time. No DB or web server needed; keys are masked; exit 0/1/2. Use it at the office with new certs/LiteLLM.
- **Verified on the sample (304L):** N 0.074, Co 0.29, HB 163 with accurate boxes; stable measurements: product form/size, weight, grain size 6-8, intergranular test, heat treatment, quantity 1, lot, mill. Response ~900 tokens vs 8000 max. E2E save: typed columns, 11 measurement rows, evidence Edited/Confirmed flags all correct (test row deleted afterwards; P02V untouched).
- **NOT verified (needs more PDFs):** impact/Charpy extraction (only manual add was tested), ppm→% conversion, carbon-steel certs, other vendors' layouts, LiteLLM/Bedrock. Optional measurements vary run to run (melt practice, reduction ratio, mill name, gauge length, test direction). Highlights on inline running text (e.g. grain size) are looser than table-cell boxes.
- **Not built:** per-vendor catalogs, units conversion beyond ppm→%, multiple values per property (e.g. several Charpy specimens, several hardness readings), editing saved certs.

## Repository, CI and branch rules

- **Repo:** https://github.com/amanoos/OCR_AI (public), default branch `main`. The git root is this folder (`cert-extraction/`); the parent folder `e:\Workspace\OCR_AI` is not part of the repo.
- **Never commit:** API keys, the connection string, uploaded vendor PDFs (`wwwroot/uploaded-certs/*` is ignored; only `.gitkeep` is tracked), `bin/`, `obj/`. Secrets live in user-secrets. `.gitattributes` keeps `wwwroot/lib/**` (vendored PDF.js) byte-for-byte unchanged — don't let line-ending conversion touch those files.
- **CI:** `.github/workflows/ci.yml`, one job named **`build-and-test`** (Ubuntu, .NET 10): restore → Release build → `dotnet test tests/SteelCertExtraction.Tests`. Runs on every push to `main` and every pull request; a newer push cancels the older run; test results are uploaded as an artifact. Needs no secrets: tests use fake HTTP handlers and in-memory SQLite, so CI never calls an LLM or SQL Server. It does NOT cover the Razor UI or real SQL Server migrations. Keep the Release build at 0 warnings.
- **Branch protection on `main`:** the `build-and-test` check is **required**, the branch must be up to date before merging (`strict`), force-pushes and deletion are blocked, and **administrators are enforced too** (no bypass). Pull requests and reviews are not required (solo project).
  - **Consequence: you cannot push directly to `main`.** Work on a branch: `git switch -c <name>` → commit → `git push -u origin <name>` → `gh pr create` → wait for `build-and-test` to pass → `gh pr merge --squash --delete-branch`. If the branch is behind `main`, update it first (`git merge origin/main` or the PR's "Update branch").
  - If CI itself is broken and blocks its own fix, an admin can temporarily turn off "Do not allow bypassing the above settings" under Settings → Branches (or `gh api -X DELETE repos/amanoos/OCR_AI/branches/main/protection/enforce_admins`), then turn it back on (`-X POST` on the same path).
  - If the job is ever renamed, update the required check name in the branch protection rule, or merges will wait forever for a check that no longer exists.

## Known gaps / explicit TODOs

1. **Persistence: done and verified** (2026-11-26) — see "Persistence". Remaining persistence work is only the deferred list (saved-cert list page, editing a saved certificate, who confirmed, etc.). The `CertDb` table currently holds one test row (heat P02V, from `Sample-MTR-304L-Stainless-Steel.pdf`); delete it if you want a clean database.
2. **Vendor key resolution** — `CertUploadPage` passes `vendorKey: null` to extraction. Needs to come from whatever record (PO, vendor master, shipment) this cert upload is associated with in the real app.
3. **`LiteLlmChatClient` needs verification against the real proxy** — see table above. Test with one real cert page before trusting batch runs.
4. ~~PDF.js loaded from a CDN~~ — **fixed (2026-11-26):** PDF.js is vendored under `wwwroot/lib/pdfjs` and the web font was dropped, so the app needs no internet access.
5. **DI registration exists** (`ServiceCollectionExtensions.AddCertExtraction`) but with placeholder config; if merged into the experiment-tracker solution, reuse its LiteLLM `HttpClient` rather than keeping a second one.
9. **Uploaded PDFs are publicly served** from `wwwroot/uploaded-certs` with no auth (GUID-prefixed names only). Move to protected storage / add auth before real vendor docs. Also `.gitignore` real uploads (a sample PDF is currently in the folder).
11. ~~Schema didn't capture N, Co, hardness etc.~~ — **done (2026-11-26):** see "More steel properties". Remaining: verify with more real certs (impact/Charpy, ppm, carbon steel).
10. **No auth on the app** (so confirmations aren't attributed to a user), and no UI/component tests.
6. **Bounding box accuracy: one cert checked, looks good** (2026-11-26, sample 304L MTR: Carbon/Sulfur boxes landed exactly on the values once the viewer bug below was bypassed). A viewer CSS bug had misplaced every highlight (flex `align-items: stretch` enlarged the canvas to 1197×923 vs its 665×513 pixel size; fixed in `cert-review.css` with `align-items: flex-start` + `flex: none` — **fix applied and verified in the browser: whole page now shows uncropped and the Carbon highlight lands exactly on `.014`**). Still test against 2-3 real certs per vendor and see how close the model's boxes land before treating the highlight UX as done. If boxes are unreliable, the documented fallback is: locate text via a secondary OCR pass and fuzzy-match against the extracted value (see prior discussion in chat history if picked up via conversation_search — search for "bounding box" or "steel cert").
7. **`MissingRequiredFields` only checks `HeatNumber` and `Grade`** — review whether other fields (e.g. cert date, certifying lab) should also be treated as required given actual compliance needs.
8. **No handling yet for PDFs containing multiple certs / multiple heat numbers in one file** beyond the prompt instructing the model to return multiple array entries — untested with a real multi-heat document.

## Suggested immediate next steps for Claude Code

(Scaffold, NuGet packages, DI registration and PDF.js script tags are done; project builds clean as of 2026-11-19.)

**Smoke-test status (as of 2026-11-19):**
- ✅ Direct Anthropic: sample MTR (`Sample-MTR-304L-Stainless-Steel.pdf`) extracts end to end. Two bugs found and fixed during it: `temperature` rejected by the API, and null numeric values (e.g. `vanadium_pct`) crashing the parser — numeric fields are now `decimal?` and missing values display as "—".
- ⏳ LiteLLM/Bedrock: not yet run against the real proxy (needs real BaseUrl/model id).
- ⏳ Still to inspect on the working extraction: value accuracy vs. the PDF, and whether highlight boxes land in the right region in the browser.

Next steps:
1. (Done) Persistence save flow verified end to end — see Known gap 1.
2. Run the same cert through `Llm:Provider = LiteLlm` with the real proxy — follow the "Office test checklist" above (start with `--check-llm`).
3. Check extracted values against the PDF and the bounding-box highlights (hover fields in the review screen).
4. Then the deferred persistence items / known gaps above (saved-cert list page, protected PDF storage, auth).

## Relevant user context (from prior conversation, for continuity)

- User's primary stack: Blazor Server, ASP.NET Core, EF Core, MudBlazor, C#, SQL Server (onion architecture).
- AI inference at work goes through a corporate LiteLLM proxy → AWS Bedrock — **never** a direct Anthropic API key at work.
- This cert-extraction feature is for a die manufacturing experiment tracking system the user is building, which already has a working LiteLLM/Bedrock integration for streaming experiment summaries — reuse that plumbing rather than reinventing it.
- Volume is low (a few PDFs/week), only 1-3 vendor formats — this justifies the "prompt-based extraction" approach over training custom Document AI models, and justifies skipping a second OCR pass for bbox precision unless testing proves it necessary.
