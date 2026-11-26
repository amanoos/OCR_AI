# Implementation Plan: Multi-Provider LLM Access (Anthropic direct + LiteLLM/Bedrock)

Source spec: SPEC.md

## Overview
Add a native-Messages-API `AnthropicChatClient` next to the existing `LiteLlmChatClient`, selected by `Llm:Provider` config at DI registration. Extraction, UI and domain code stay untouched. Add the project's first test project (fake `HttpMessageHandler`) to lock in both clients' request/response contracts.

## Architecture Decisions
- **Seam stays `ILlmChatClient`.** Providers differ only in wire format, so no new abstraction. Interface moves to its own file so both clients reference it cleanly.
- **Options class (`LlmOptions`) bound from `Llm:*`**, validated at startup (fail fast, message names the missing key). Legacy `LiteLlm:*` keys are read as a fallback (spec default; open question 2 — assumed "keep fallback", cheap to remove later).
- **Default Anthropic model id `claude-sonnet-5-5`** (open question 1 — assumed; it's config, trivially changed).
- **Each task leaves build + tests green.**
- **No new runtime NuGet packages.** Test project adds xUnit (+ test SDK) only, implied by the approved testing strategy.

## Dependency Graph
```
T1 interface + options ── T2 test project + LiteLlm tests ── T3 AnthropicChatClient (+tests)
                                                                   └── T4 DI provider selection (+tests)
                                                                          └── T5 config/docs/smoke test
```

## Task List
Tracked in `tasks/todo.md`.

### Phase 1: Foundation
- [x] Task 1: Move `ILlmChatClient`, add `LlmOptions`
- [x] Task 2: Test project + `LiteLlmChatClient` tests (incl. base-path regression)

### Checkpoint: Foundation — build 0 warnings, tests green, behavior unchanged

### Phase 2: New provider
- [x] Task 3: `AnthropicChatClient` with tests
- [x] Task 4: Provider selection + fail-fast validation, with tests

### Checkpoint: Core — both providers resolvable by config; all tests pass

### Phase 3: Ship
- [ ] Task 5: Config migration, docs, manual smoke tests

### Checkpoint: Complete — all SPEC success criteria met

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| `tests/` lives inside the web project folder, so the web csproj's default globbing compiles test sources and breaks the build | High | T2 adds `<Compile Remove="tests\**" />` (and Content/EmbeddedResource/None removes) to `SteelCertExtraction.csproj`; verify with a clean build |
| Anthropic API details (headers, `anthropic-version`, content-block shapes, model id) drift from the spec | Med | Verify against current Anthropic docs while implementing T3; version is config |
| Legacy `LiteLlm:*` fallback silently masks misconfig | Low | Log which config source was used at startup |
| Neither client is verified against a live API (no real keys in tests) | Med | T5 manual smoke test per provider with one real cert page |
| Anthropic key leaks into committed config | High | Empty `ApiKey` in appsettings; user-secrets documented; never-commit boundary |

## Open Questions
- Confirm assumed defaults: model id `claude-sonnet-5-5`; keep legacy `LiteLlm:*` fallback.
