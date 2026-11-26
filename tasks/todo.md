# Task List — Multi-Provider LLM Access (see tasks/plan.md, SPEC.md)

## Task 1: Move `ILlmChatClient` and add `LlmOptions` — DONE (build 0 warnings/0 errors)
**Description:** Move `ILlmChatClient` (and `LlmCallException`) into `Services/ILlmChatClient.cs`; add `Services/LlmOptions.cs` modeling `Llm:Provider` plus `LiteLlm` and `Anthropic` sections. No behavior change.
**Acceptance criteria:**
- [ ] `ILlmChatClient` lives in its own file; `CertExtractionService.cs` no longer declares it
- [ ] `LlmOptions` has Provider enum/string and per-provider BaseUrl/ApiKey/ModelId (+ Anthropic `ApiVersion`, default `2023-06-01`)
**Verification:**
- [ ] `dotnet build` succeeds, 0 warnings
- [ ] App starts and behaves as before
**Dependencies:** None
**Files:** `Services/ILlmChatClient.cs`, `Services/LlmOptions.cs`, `Services/CertExtractionService.cs`, `Services/LiteLlmChatClient.cs`
**Scope:** Small

## Task 2: Test project + `LiteLlmChatClient` tests — DONE (6 tests pass, build clean)
**Description:** Create `tests/SteelCertExtraction.Tests` (xUnit) referencing the web project; exclude `tests\**` from the web csproj compile globs. Add fake-`HttpMessageHandler` tests for `LiteLlmChatClient`.
**Acceptance criteria:**
- [ ] Web project still builds cleanly with tests folder present
- [ ] Test: with BaseAddress `https://h/v1/`, request URL is `https://h/v1/chat/completions` (regression)
- [ ] Tests: body has system message + text part + `image_url` data-URI parts; success returns content; non-2xx and empty content throw `LlmCallException`
**Verification:**
- [ ] `dotnet test tests/SteelCertExtraction.Tests` green
- [ ] `dotnet build SteelCertExtraction.csproj` 0 warnings from a clean state
**Dependencies:** Task 1
**Files:** `SteelCertExtraction.csproj`, `tests/SteelCertExtraction.Tests/*.csproj`, `FakeHttpHandler.cs`, `LiteLlmChatClientTests.cs`
**Scope:** Medium

### Checkpoint: After Tasks 1-2
- [ ] Build + tests green, behavior unchanged, review with human

## Task 3: `AnthropicChatClient` — DONE (13 tests pass; API shape written from known Messages API, not checked against live docs)
**Description:** Implement native Messages API client per SPEC "Behavior Contract". Verify current API details against Anthropic docs while implementing.
**Acceptance criteria:**
- [ ] POSTs to relative `v1/messages`; sends `x-api-key` and `anthropic-version`; top-level `system`; `max_tokens`; `temperature 0`
- [ ] Images sent as base64 `image` blocks (`image/png`); text block for user prompt
- [ ] Response text = concatenated `text` blocks; non-2xx / empty → `LlmCallException`
- [ ] `ModelIdentifier` returns configured model id
**Verification:**
- [ ] Unit tests (fake handler) for URL, headers, body shape, parsing, error paths pass
- [ ] `dotnet build` 0 warnings
**Dependencies:** Tasks 1, 2
**Files:** `Services/AnthropicChatClient.cs`, `tests/.../AnthropicChatClientTests.cs`
**Scope:** Small–Medium

## Task 4: Provider selection and fail-fast validation — DONE (25 tests pass, build clean)
**Description:** Rework `AddCertExtraction` to bind `LlmOptions`, register a named HttpClient for the active provider only, and resolve `ILlmChatClient` accordingly. Read legacy `LiteLlm:*` keys as fallback and log which source was used. Validate at startup.
**Acceptance criteria:**
- [ ] `Provider=LiteLlm` → `LiteLlmChatClient`; `Provider=Anthropic` → `AnthropicChatClient`
- [ ] Unknown provider, or missing ApiKey/ModelId/BaseUrl for the active provider → startup exception naming the missing key (LiteLLM ApiKey may stay optional if proxy needs none — confirm against existing behavior)
- [ ] Legacy-only `LiteLlm:*` config still works
**Verification:**
- [ ] DI tests (build `ServiceProvider` from in-memory config) for each case pass
- [ ] `dotnet build` 0 warnings; app starts with the default config
**Dependencies:** Task 3
**Files:** `Services/ServiceCollectionExtensions.cs`, `Services/LlmOptions.cs`, `tests/.../ProviderSelectionTests.cs`
**Scope:** Medium

### Checkpoint: After Tasks 3-4
- [ ] Both providers resolvable by config; all tests green; review with human

## Task 5: Config, docs, manual smoke test — PARTIAL: config + docs done and startup verified for both providers; live smoke test pending user credentials
**Description:** Update `appsettings.json` to the new `Llm:*` shape (empty keys), document user-secrets usage, update CLAUDE.md and mark SPEC items done; manually smoke-test each provider with one real cert page.
**Acceptance criteria:**
- [ ] `appsettings.json` has `Llm` section, no secrets
- [ ] CLAUDE.md describes provider switch, config keys, and test command
- [ ] Manual: sample cert extracts end to end with `Provider=LiteLlm` and with `Provider=Anthropic` (requires user-supplied keys)
**Verification:**
- [ ] `dotnet build` + `dotnet test` green
- [ ] Smoke test results recorded in CLAUDE.md (pass/fail per provider; note anything unverified)
**Dependencies:** Task 4
**Files:** `appsettings.json`, `CLAUDE.md`, `SPEC.md`
**Scope:** Small

### Checkpoint: Complete
- [ ] All SPEC success criteria met; ready for review
