# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial repository scaffolding: solution, `Subconscious.Engine`, `Subconscious.Tools.Desktop`,
  `Subconscious.Host.Cli`, and `Subconscious.Engine.Tests` projects targeting .NET 10.
- `translation.md`: full phased plan for porting the Python `subconscious` engine and its
  client family (desktop, mobile, web, headless, browser extension) to .NET.
- CLI entry point (`Subconscious.Host.Cli`) with `engine`/`desktop`/`web`/`code` subcommands
  and `--dev`/`--no-api` flags, mirroring the Python CLI's shape (stubs pending later phases).
- `EngineConfig` and `EngineHost` composition-root scaffolding (`Subconscious.Engine`).
- VS Code debug configuration for launching the engine host in development mode.
- `--headless` flag on the `engine`/`desktop` subcommands, pulled forward from the original
  Phase 6 scope: skips the desktop GUI window only (a no-op today). The system tray icon is
  unaffected and always shown on platforms that support one.
- System tray icon support (`Subconscious.Desktop.Tray`, `ITrayIconService`): a real
  `NotifyIcon`-backed implementation on Windows (`net10.0-windows`), a no-op fallback everywhere
  else, and an "Open Subconscious" / "Exit" context menu wired into the engine host. Shown
  unconditionally (independent of `--headless`) whenever a tray backend is available. Mirrors
  `desktop/tray.py`'s `pystray`-based tray menu from the Python app.
- `EngineTrayCoordinator` in `Subconscious.Host.Cli` wiring the tray icon's "Exit" action to
  gracefully stop the hosted engine.

### Fixed
- `Directory.Build.props` no longer sets a bare `<TargetFramework>`, which was silently
  preventing every multi-targeted project (`Subconscious.Desktop.Tray`, `Subconscious.Host.Cli`)
  from ever building their second (`net10.0-windows`) target framework.
- `EngineTrayCoordinator`'s icon path now matches where the build actually copies
  `favicon.ico` (`Assets/favicon.ico`, preserving the source folder), instead of a flattened
  path that never resolved — the tray icon previously silently fell back to the generic
  Windows application icon.

### Added (Phase 2 — model & agent layer)
- Decided [LLM Tornado](https://github.com/lofcz/LlmTornado) as the provider-agnostic model SDK
  (`agent.py`'s `AgentManager` equivalent), driven through its `Microsoft.Extensions.AI`
  bridge for the interactive chat/tool loop. See translation.md §4.4 for the full decision,
  including how an optional AG-UI endpoint (Phase 5) composes on top of the same `IChatClient`.
- `Subconscious.Engine.Agents.AgentManager`: builds an `IChatClient` from a `ModelConfig`,
  covering direct Tornado providers (OpenAI, Anthropic, Google, Groq, Mistral, xAI, Cohere,
  DeepSeek, OpenRouter, Perplexity, and more) and OpenAI-compatible custom endpoints
  (Ollama, LM Studio, Azure AI Foundry, Fireworks AI, GitHub Models, LiteLLM, Nebius AI Studio,
  SambaNova, Together AI, Alibaba Cloud Model Studio).
- `EchoChatClient`: dev/test double implementing `IChatClient` directly, replacing
  `agent.py`'s `EchoProvider`.
- `Subconscious.Engine.Approval`: `OperationKind`, `OperationClassifier` (ported 1:1 from
  `tools/__init__.py`'s `classify_operation`), `ApprovalConfig` (ported from `engine.py`'s
  `_DEFAULT_APPROVAL_CONFIG`/`_normalize_approval_config`), and `ApprovalGate` (wraps tool
  `AIFunction`s in `Microsoft.Extensions.AI.ApprovalRequiredAIFunction` per the resolved policy).
- 52 unit tests covering the classifier, approval config, provider catalog, `EchoChatClient`,
  and `AgentManager` (including that Bedrock/Hugging Face correctly throw `NotSupportedException`
  rather than silently mis-routing).

### Known gaps (flagged, not silently dropped)
- **AWS Bedrock has no LLM Tornado connector** as of this writing (confirmed against Tornado's
  own `FeatureMatrix.md`) — `agent.py`'s Bedrock support has no 1:1 port yet.
  `ProviderCatalog.Resolve("bedrock")` throws `NotSupportedException` with a pointer to
  translation.md §4.4/§9 rather than mis-routing the request. Same treatment for Hugging Face
  (no equivalent OpenAI-compatible endpoint shape in Tornado's Custom provider).

[Unreleased]: https://github.com/Ancilla-Company/Subconscious-net/commits/main
