# Task List — Office Readiness (see tasks/plan-office-readiness.md, SPEC-office-readiness.md)

(PDF.js files already downloaded + verified; see plan.)

## O1: Use local PDF.js, drop Google Fonts — DONE (verified in Chrome: pdfjsLib 3.11.174 from lib/pdfjs, local worker, 2-page PDF opened and page 1 rendered; all page resources from localhost only; no external refs left in App.razor)
**Description:** Point `App.razor` at `lib/pdfjs/pdf.min.js` and `lib/pdfjs/pdf.worker.min.js`; remove the Google Fonts `<link>`.
**Acceptance criteria:**
- [ ] No `http(s)://` references remain in `Components/App.razor`
- [ ] Worker source is the local path, same version as the library
- [ ] In Chrome: page loads with zero requests to non-localhost hosts; `pdfjsLib` defined; `getDocument` on a stored sample PDF resolves with 2 pages
**Verification:** `dotnet build`; Chrome network list + console check (no API call needed)
**Dependencies:** None
**Files:** `Components/App.razor`
**Scope:** Small

## O2: LiteLLM `temperature` safety — DONE (11 new tests; 65 total pass, 0 warnings)
**Description:** Add `Llm:LiteLlm:SendTemperature` (default true). In `LiteLlmChatClient`, on a 400 whose body mentions `temperature`, retry once without it and omit it for the rest of the instance.
**Acceptance criteria:**
- [ ] `SendTemperature=false` → field never in the body
- [ ] Default → unchanged request (temperature 0)
- [ ] 400 mentioning `temperature` → exactly one retry without it → success returned
- [ ] 400 not mentioning it → no retry, `LlmCallException` as today
- [ ] After a fallback, later calls on the same client don't send temperature
**Verification:** new unit tests (fake handler records every request); full suite green
**Dependencies:** None
**Files:** `Services/LlmOptions.cs`, `Services/ServiceCollectionExtensions.cs`, `Services/LiteLlmChatClient.cs`, `tests/.../LiteLlmChatClientTests.cs`, (`ProviderSelectionTests.cs` if needed)
**Scope:** Medium

### Checkpoint: After O1–O2 — build 0 warnings, tests green, review with human

## O3: `--check-llm` command — DONE (14 new tests; 79 total pass, build 0 warnings; run for real: Anthropic config → exit 0 "OK" in ~1.6s incl. image; broken LiteLLM config → exit 1 with hint; key never printed; normal startup unchanged)
**Description:** `LlmConnectionCheck` runs one tiny request (text + 1×1 PNG) through the configured `ILlmChatClient` and prints provider, base URL, model, outcome, latency, reply/error; `Program.cs` runs it for `--check-llm` before persistence registration and exits.
**Acceptance criteria:**
- [ ] Success → exit code 0 and prints the reply; failure (non-2xx / exception) → non-zero with the error body
- [ ] The API key never appears in output (even if an error body echoes it)
- [ ] No database connection string needed; web server not started
- [ ] Normal startup (no flag) unchanged
**Verification:** unit tests with fake clients; manual `dotnet run … -- --check-llm` against current Anthropic config; normal startup re-checked
**Dependencies:** O2
**Files:** `Services/LlmConnectionCheck.cs`, `Program.cs`, `tests/.../LlmConnectionCheckTests.cs`
**Scope:** Small–Medium

## O4: Docs + office-day checklist — DONE (CLAUDE.md: file table, LiteLLM options, Office test checklist, gap 4 closed; spec status updated)
**Description:** CLAUDE.md: link the new spec/plan, document `SendTemperature` and `--check-llm`, add a short office-day checklist (secrets to set, run the check, then the app, what to paste back if it fails). Mark spec implemented.
**Acceptance criteria:**
- [ ] Checklist covers: `Llm:Provider`/`BaseUrl` (ends `/v1/`)/`ModelId`/`ApiKey` secrets, DB secret if different, `--check-llm`, then the real run
- [ ] SPEC-office-readiness.md status updated
**Verification:** build + tests green
**Dependencies:** O3
**Files:** `CLAUDE.md`, `SPEC-office-readiness.md`
**Scope:** Small

### Checkpoint: Complete — success criteria 1–5 of the spec met
