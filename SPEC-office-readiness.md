# Spec Delta: Office Readiness (offline assets, LiteLLM temperature safety, LLM connection check)

Status: implemented and verified (2026-11-26) — PDF.js self-hosted (verified in Chrome: no external hosts), LiteLLM temperature fallback + `SendTemperature` option (unit-tested), `--check-llm` (run live against direct Anthropic; failure path run against a broken LiteLLM config). The real LiteLLM/Bedrock proxy has still not been tested — that is the office run.

## Objective
The LiteLLM/Bedrock path can only be tested from the office network, where (a) the CDNs the app loads PDF.js and fonts from may be blocked, (b) the proxy/model may reject `temperature` as direct Anthropic did, and (c) there will be little time to debug. Remove those three risks beforehand so the first office run is about the proxy itself, not the app shell.

Inherits everything else from SPEC.md and SPEC-persistence.md.

## 1. Self-host PDF.js and drop the Google Font

**Current:** `Components/App.razor` loads PDF.js 3.11.174 from `cdnjs.cloudflare.com` and Roboto from `fonts.googleapis.com`. If blocked, the review screen shows no PDF (and the app falls back silently).

**New:**
- Vendor two files into `wwwroot/lib/pdfjs/`: `pdf.min.js` (≈0.3 MB) and `pdf.worker.min.js` (≈1.1 MB), version **3.11.174** (same version as now, so behavior is unchanged), plus a `NOTICE.txt` recording the version, source URL and license (Apache-2.0).
- `App.razor` loads them from `lib/pdfjs/...` (no external script/worker URL).
- Remove the Google Fonts `<link>`; MudBlazor's font stack falls back to system sans-serif. (Self-hosting Roboto is **deferred** unless the look matters.)
- Result: the app makes **zero requests to external hosts**.

**Download (needs your approval):** the two files above from `https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/` (public, Apache-2.0 licensed), into `wwwroot/lib/pdfjs/`.

## 2. LiteLLM `temperature` safety
**Current:** `LiteLlmChatClient` always sends `temperature = 0`; direct Anthropic rejected it for this model with a 400, and the proxy/model at the office may too.

**New (both, small):**
- Config `Llm:LiteLlm:SendTemperature` (bool, default `true`, current behavior). `false` omits the field.
- Automatic fallback: if the response is a 400 whose body mentions `temperature`, retry the request **once** without it, and omit it for the rest of that client's lifetime. Any other error behaves as today. (Cost of the fallback: one extra failed round trip per new client instance.)

## 3. LLM connection check
**New:** a command-line check, no UI and no exposed endpoint:
```
dotnet run --project SteelCertExtraction.csproj -- --check-llm
```
Builds the app's real DI container (so it exercises the exact configured provider, URL, key and model), sends one tiny request — a text prompt plus a **1×1 PNG** (so image passing through the proxy is tested too) — and prints: provider, base URL, model id, HTTP outcome, latency, and the model's reply or the error body. API keys are never printed. Exit code 0 on success, non-zero on failure. Does not start the web server, touch the database, or need a real PDF.

## Success Criteria
1. After the change, loading the app and reviewing a cert makes **no** requests to external hosts (verified in the browser's network list), and the PDF still renders with highlights.
2. Existing behavior is unchanged when `SendTemperature` is true and the proxy accepts temperature (tests).
3. A 400 mentioning `temperature` triggers exactly one retry without it and succeeds; a 400 not mentioning it does not retry; with `SendTemperature=false` the field is never sent (tests).
4. `--check-llm` prints a clear success or failure for both providers and never prints a key; against a fake handler in tests it returns 0 on success and non-zero on a 4xx/5xx.
5. `dotnet build` 0 warnings; all tests pass.

## Boundaries
- Always: keep PDF.js at 3.11.174; keep the license notice; mask secrets in all output.
- Ask first: upgrading PDF.js; adding NuGet packages (none expected); any change to the extraction prompt.
- Never: print or log API keys or connection strings; send real cert contents in the connection check; expose the check over HTTP.

## Out of Scope (Deferred)
- Self-hosting Roboto / any other font.
- Retry/backoff for other errors, rate-limit handling.
- A UI button for the connection check.
- Version control setup (separate decision).

## Open Questions
1. Are you OK with the system-font fallback instead of Roboto?
2. Approve the two PDF.js downloads (above)?
