# Subconscious Terminal architecture

## Delivered design

`Subconscious.Terminal` has two renderer modes over the shared engine client:

- The ANSI renderer is the default interactive mode. It keeps normal terminal scrollback and the terminal's default background. Completed transcript blocks are written once through Spectre.Console; only the active response, status, picker/approval overlay, and composer form a small live ANSI tail. Slash-command pickers remain interactive without introducing an application-owned scrollbar or full-screen surface.
- `--plain` is the line-oriented fallback. Redirected streams and `TERM=dumb` select noninteractive behavior automatically.

The interactive implementation serializes engine/input events, correlates turns, and coalesces streaming deltas. The framework-neutral `Subconscious.Client` assembly continues to own discovery, authenticated REST, WebSocket streaming, reconnect, models, cancellation, and tool approvals.

## User interaction

- Enter sends; Shift+Enter, Ctrl+Enter, or Ctrl+J inserts a newline.
- Escape or `/cancel` cancels a running turn.
- Ctrl+C clears a draft, cancels a turn, or exits when idle.
- Ctrl+L or `/clear` explicitly clears the visible screen.
- Up/Down recalls prompt history; Tab completes slash commands.
- `/workspaces`, `/threads`, and `/models` open keyboard pickers.
- `/workspace`, `/thread`, and `/model` also accept a one-based index, id, or partial label.
- Tool approval requests are shown inline and resolved with arrows plus Enter, `y`, or `n`.

## ANSI rendering invariants

1. Never call `Console.Clear` during normal rendering.
2. Sanitize engine-controlled control characters before terminal output.
3. Keep normal terminal scrollback; do not enter the alternate screen.
4. Repaint only the bounded live tail and coalesce token updates to roughly 40 FPS.
5. Restore cursor, encoding, Ctrl+C, and Windows console modes on every exit path.
6. Keep an operational plain-text path for pipes, CI, logs, and unsupported terminals.

## Engine behavior

The client discovers `runtime.json`, probes `/api/v1/health`, and can launch `subconscious engine --headless`. REST provides workspaces, threads, history, models, and persisted terminal selection. `/api/v1/events` carries correlated chat deltas, completion, cancellation, errors, and approval requests.

A draft sends `workspace_uuid`; the engine creates and names its thread. Existing conversations send `thread_uuid`. The UI refreshes thread metadata after completion and persists active workspace, thread, and model under the `terminal` client scope.

## Future extensions

The renderer already has structured committed/live boundaries and overlays. Additional engine frame types can add command output, diff previews, diagnostics, file references, or task plans without changing terminal ownership or repaint strategy.
