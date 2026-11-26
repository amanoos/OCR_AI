# Implementation Plan: Office Readiness

Source spec: SPEC-office-readiness.md (spec approved + PDF.js downloads approved 2026-11-26)
Task list: tasks/todo-office-readiness.md

## Overview
Three small changes so the first LiteLLM/Bedrock run at the office tests the proxy, not the app shell: self-host PDF.js and drop the Google Font (O1), make the LiteLLM client safe against a `temperature` rejection (O2), and add a `--check-llm` command-line connection check (O3). O4 documents an office-day checklist.

## Already done
- PDF.js 3.11.174 `pdf.min.js` (320,004 bytes) and `pdf.worker.min.js` (1,087,212 bytes) downloaded to `wwwroot/lib/pdfjs/`, verified against cdnjs SRI hashes, `NOTICE.txt` written.

## Architecture Decisions
- **O1:** plain (non-fingerprinted) `lib/pdfjs/...` URLs in `App.razor`; the worker path must match the library version exactly, so both files are always replaced together. Served by the existing `UseStaticFiles`.
- **O2:** the 400-retry lives inside `LiteLlmChatClient` (provider-specific wire behavior stays in the client). Request is rebuilt via a local factory since an `HttpRequestMessage` can't be re-sent. Fallback matches `temperature` case-insensitively in a 400 body, retries once, remembers for that instance.
- **O3:** logic in a testable `LlmConnectionCheck` class (takes `ILlmChatClient` + a `TextWriter`, returns an exit code). `Program.cs` handles `--check-llm` **before** persistence registration with a minimal container (`AddLogging` + `AddCertExtraction`), so no database or web host is needed. Secrets masked by replacing the configured key in any output.
- Each task leaves build and tests green.

## Dependency Graph
```
O1 self-host PDF.js        (independent)
O2 temperature safety ─┐
                       ├─ O3 --check-llm (uses the client, exercises O2's fallback)
                       └─ O4 docs + office checklist
```

## Task List
Tracked in tasks/todo-office-readiness.md.

- [x] O1: Use local PDF.js, drop Google Fonts
- [x] O2: LiteLLM `temperature` safety (config + one-shot fallback), with tests
### Checkpoint: after O1–O2 — build 0 warnings, tests green, app makes no external requests
- [x] O3: `--check-llm` command, with tests
- [x] O4: Docs + office-day checklist
### Checkpoint: Complete

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| PDF.js worker fails to load from the local path (base href, MIME type) | High | Verify in Chrome: `pdfjsLib` defined, `getDocument` on a stored PDF succeeds, network list shows only localhost — no API call needed |
| The review page still needs an extraction (one API call) to confirm highlights end to end | Low | Optional final check; PDF.js load verified without it |
| Retry mis-triggers on unrelated 400s | Med | Match only bodies mentioning `temperature`; test that other 400s don't retry |
| `--check-llm` prints a secret via an error body | High | Mask the configured API key in all output; test it; never log request headers |
| `Program.cs` change breaks normal startup | Med | Check-mode branch only when the flag is present; re-run normal startup check after |
| Real LiteLLM behavior still untestable here | Med | Tests use a fake handler; the true test is at the office, which is the point of O3 |

## Open Questions
- None blocking. (System-font fallback accepted per approval.)
