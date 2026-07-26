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

[Unreleased]: https://github.com/Ancilla-Company/Subconscious-net/commits/main
