# Subconscious.Chat custom transcript plan

> **Implementation update (August 2026):** The experiment is now split into
> `Subconscious.Chat.Web` and `Subconscious.Chat.Native`, both backed by the platform-neutral
> `Subconscious.Chat` contract/projection library. Web uses one persistent WebView DOM for native
> browser selection and interactive HTML bubbles. Native uses a custom-drawn MAUI `GraphicsView`
> with retained character geometry and coordinated cross-message selection; it does not use
> `RichTextBlock`, `RichEditBox`, `CollectionView`, or WebView. `Subconscious.Chat.Debug` launches
> either renderer against the same placeholder messages. This implemented split supersedes the
> single-document recommendation in the exploratory sections below.

## Goal

Build a reusable .NET MAUI chat transcript component in `src/Subconscious.Chat` that can replace the transcript portion of `Subconscious.Desktop/Views/ChatPanelView.xaml` without changing `ChatViewModel` behavior. It must preserve the compact, left/right-aligned bubbles shown in the reference screenshot, render the Markdown features already handled by `MarkdownMessageView`, and—most importantly—allow one native text selection to start in one message and continue through later messages, as shown in the selection screenshot.

This is a transcript component, not a second chat application. The existing header, composer, model picker, send/stop controls, tool-policy overlay, and `ChatViewModel` commands remain owned by Desktop.

## Key design constraint

A `CollectionView`, `BindableLayout`, or stack of independently selectable labels cannot provide a single native selection across messages. Each child text control owns a separate selection range. Therefore, the Windows implementation must render all selectable transcript text through **one native document control**.

The recommended first implementation is a WinUI `RichTextBlock` with `IsTextSelectionEnabled="True"` inside a native `ScrollViewer`. Every ordinary message and selectable tool detail is represented in the same `Blocks` collection. If the prototype proves that `RichTextBlock` cannot provide acceptable bubble styling or selection restoration during streaming, use one read-only `RichEditBox`/`ITextDocument` instead; do not fall back to one text control per bubble.

This creates an explicit trade-off: exact independent MAUI `Border` bubbles, embedded text controls, and stock native cross-message selection cannot all coexist. The component should reproduce the visual hierarchy—alignment, width, fill, padding, spacing, timestamps, and copy/tool actions—as closely as the single-document model allows, while treating uninterrupted selection as the non-negotiable requirement.

## Scope and acceptance criteria

### Required

- Bind to the same message collection used by `ChatViewModel.Messages`.
- Support collection Add, Remove, Replace, Move, Reset, and history reloads.
- React to message property changes, especially streamed `Content` updates and tool expansion changes.
- Display user messages on the right and assistant/tool messages on the left, with a maximum visual width equivalent to the current 525 px bubbles.
- Preserve current semantic colors: `SurfaceColor`, `PanelBackgroundColor`, `PrimaryTextColor`, `SecondaryTextColor`, `DividerColor`, `AccentColor`, `HoverColor`, `UserBubbleColor`, `AssistantBubbleColor`, and `ErrorColor`.
- Render headings, paragraphs, bold, italic, links, inline code, fenced/code blocks, quotes, ordered/unordered lists, tables, thematic breaks, and line breaks.
- Allow mouse drag selection, keyboard extension, Ctrl+A, and Ctrl+C across two or more messages in chronological order.
- Keep cross-message selection working when tool messages occur between ordinary messages. Expanded tool input/output should be selectable if visible.
- Preserve an active selection and the reader's scroll position when assistant content streams, whenever the underlying native API permits it.
- Auto-follow new output only when the viewport was already at the bottom and the user is not selecting or reading older content.
- Support light/dark/platform theme changes without rebuilding a WebView document.
- Keep whole-message copy and tool expand/collapse actions accessible independently of text selection.
- Show the empty-state text and retain pending tool approval as a transcript-adjacent, scrolling item.
- Provide an explicit non-Windows fallback or renderer so Desktop's Mac Catalyst target remains buildable.

### Out of scope for the first increment

- Replacing the header, composer, model picker, send/stop behavior, or tools-policy overlay.
- Changing engine transport, persistence, or `ChatViewModel` conversation semantics.
- Full virtualization. A single native document necessarily keeps the visible thread in one text owner; performance limits will be measured before adding windowing.
- Pixel-perfect parity for nested horizontal code/table scrolling inside the selectable document. Readability and copyable text take priority in v1.
- Reply/quote actions from the second screenshot unless separately requested.

## Existing contracts to preserve

- `ChatViewModel.Messages` is a stable `ObservableCollection<MessageViewModel>` that is cleared/repopulated on thread selection.
- Sending appends a user message followed by an empty assistant message. Streaming mutates that assistant instance through `MessageViewModel.AppendDelta`, which raises `PropertyChanged` for `Content`.
- `MessageViewModel` exposes immutable `Role`/`CreatedAt`, derived `Timestamp`, `IsUser`, `IsTool`, tool title/input/output, `IsToolExpanded`, `ToolExpansionGlyph`, and `ToggleToolExpandedCommand`.
- `ChatViewModel.ThemeRevision` changes after semantic theme resources are refreshed and should trigger native palette reapplication.
- Pending approval currently lives in the `CollectionView.Footer`; it must continue to scroll with the transcript and invoke `ApproveToolCommand`/`DenyToolCommand`.
- `MarkdownMessageView` uses Markdig Advanced Extensions and defines the current Markdown feature baseline, but it cannot be reused internally because it creates many independent MAUI controls.

## Proposed architecture

### 1. Project and dependency direction

Create `src/Subconscious.Chat/Subconscious.Chat.csproj` as a MAUI class library and add it to `Subconscious.slnx`.

- Match Desktop's OS-conditional target frameworks: Windows builds `net10.0-windows10.0.19041.0`; macOS builds `net10.0-maccatalyst`.
- Reference pinned `Microsoft.Maui.Controls` through `$(MauiVersion)` and `Markdig` `0.40.0` to match Desktop.
- Add a `ProjectReference` from Desktop to Chat.
- Do not reference Desktop or Engine from Chat; that would create a dependency cycle and couple the component to transport concerns.

Introduce a small presentation contract in Chat, for example `IChatTranscriptMessage : INotifyPropertyChanged`, exposing the properties required by the renderer. Have Desktop's existing sealed `MessageViewModel` implement it. `ChatViewModel.Messages` remains unchanged; generic covariance is not available for `ObservableCollection<T>`, so the component's `ItemsSource` should accept `IEnumerable`/`INotifyCollectionChanged` and validate/adapt each item to the interface.

Suggested project shape:

```text
src/Subconscious.Chat/
  Subconscious.Chat.csproj
  ChatTranscriptView.cs
  Contracts/IChatTranscriptMessage.cs
  Documents/ChatDocument.cs
  Documents/ChatDocumentBuilder.cs
  Documents/ChatDocumentOffsetMap.cs
  Rendering/ChatTranscriptController.cs
  Platforms/Windows/ChatTranscriptPlatformView.cs
  Platforms/Windows/WinUiDocumentRenderer.cs
  Platforms/MacCatalyst/ChatTranscriptPlatformView.cs
  chat_plan.md
```

Names may change during implementation, but keep document projection, collection synchronization, and native rendering separate.

### 2. Public MAUI control

Expose a `ChatTranscriptView : View` with bindable properties/events rather than binding directly to `ChatViewModel`:

- `ItemsSource`: message collection.
- `ThemeRevision`: invalidation signal for resolving semantic resources again.
- `EmptyText`: defaults to `Start a conversation.`.
- `MaximumBubbleWidth`: defaults to `525`.
- Optional `Footer`: hosts Desktop's approval view below the transcript document.
- Optional command/event hooks for whole-message copy and tool expansion if those actions cannot bind directly through native elements.

The control owns lifecycle-safe subscriptions. On `ItemsSource` replacement or handler disconnection it must unsubscribe from both collection and message notifications.

Desktop usage should remain small and compiled-binding friendly, conceptually:

```xml
<chat:ChatTranscriptView Grid.Row="1"
                         ItemsSource="{Binding Messages}"
                         ThemeRevision="{Binding ThemeRevision}"
                         MaximumBubbleWidth="525">
    <chat:ChatTranscriptView.Footer>
        <!-- Existing pending approval content -->
    </chat:ChatTranscriptView.Footer>
</chat:ChatTranscriptView>
```

### 3. Platform-neutral Markdown document model

Parse Markdig once into a component-owned model rather than first creating MAUI controls. The model should represent:

- Message boundary, stable message identity, role, timestamp, and tool metadata.
- Block nodes: paragraph, heading, code, quote, list, table, divider, and tool section.
- Inline nodes: text, emphasis, strong, code, link, and line break.
- A canonical plain-text projection used for selection offsets, clipboard expectations, accessibility, and tests.
- UTF-16 offsets for each message and block because WinUI text ranges use UTF-16 code-unit positions.

Define canonical separators deliberately. For example, blocks are separated by one newline, messages by two newlines, lists include visible markers, and tables use tabs/newlines. Decorative timestamps and action labels should not silently enter copied text unless product behavior explicitly requires them.

For streaming, cache each message's parsed document by message identity/content revision. Reparse and replace only the changed message region; do not parse all prior messages for every token.

### 4. Windows renderer: one selection owner

Prototype with one WinUI `RichTextBlock`:

- `IsTextSelectionEnabled = true`.
- One chronological `Blocks` collection for the full transcript.
- Apply Markdig formatting with `Paragraph`, `Run`, `Span`, `Bold`, `Italic`, `Hyperlink`, and `LineBreak`.
- Use paragraph indentation, margins, foregrounds, and available text decorations for Markdown structure.
- Represent code as monospaced paragraphs/runs with a code background where supported; otherwise use a subtle block-level background or border adjacent to the document.
- Flatten tables to aligned/tab-delimited selectable text in v1 if a native table would split selection ownership.

Bubble visuals should be implemented without placing message text in separate controls. Investigate these in order:

1. Paragraph-level background/indent/margin capabilities available in the selected WinUI control.
2. `RichEditBox` character/paragraph background formatting if `RichTextBlock` cannot paint acceptable message regions.
3. Non-text background adorners behind measured message ranges, while the single transparent document remains the only hit-tested text surface.

Keep copy buttons and tool chevrons in a separate overlay/gutter aligned to message bounds. They may be native buttons, but must not contain transcript text or intercept drag-selection except inside their own hit targets. Invoke the existing message command/content through the presentation interface.

Do not use `InlineUIContainer` for selectable message bodies: text inside an embedded control belongs to a different selection owner.

### 5. Incremental synchronization, selection, and scrolling

`ChatTranscriptController` should:

- Subscribe to `INotifyCollectionChanged` and each item's `INotifyPropertyChanged`.
- Maintain stable per-message document regions and UTF-16 offset maps.
- Coalesce high-frequency `Content` notifications on the UI dispatcher (target one render per frame, not one render per token).
- Before applying a patch, capture selection anchor/focus offsets, viewport offset, extent, and whether the view is pinned to bottom.
- Patch only the affected final message where practical.
- Restore selection from logical offsets after the patch, clamping offsets if selected content was replaced.
- Suppress auto-follow while selection is non-empty, pointer selection is active, or the viewport was not at the bottom.
- Scroll to the end after a new user/assistant message only when follow mode is active.

If `RichTextBlock` does not expose enough selection control to restore ranges after updates, this is the decision point for moving to `RichEditBox`/`ITextDocument`, not for rebuilding separate message controls.

### 6. Theme integration

Resolve MAUI semantic resources into a `ChatTranscriptPalette` and explicitly apply WinUI brushes. Native WinUI elements will not automatically follow MAUI `DynamicResource` changes.

- Reapply the palette when `ThemeRevision` changes.
- Also reapply on handler creation and relevant MAUI resource/property changes.
- Avoid recreating document content for palette-only changes; update brushes/formats in place.
- Preserve Windows selection colors unless contrast testing shows they are unreadable against bubble fills.

### 7. Tools and approval

Tool rows in the screenshot are compact metadata bubbles. Preserve that shape with title, timestamp, copy, and expand/collapse affordances.

- Collapsed: expose the tool title as a short selectable document block and place action buttons in the overlay/gutter.
- Expanded: insert `Input` and optional `Output` blocks into the same native document so selection can pass through them.
- Expansion invokes the existing command/state and patches that message region.
- Pending approval remains a MAUI/native interactive footer in the transcript's outer scrolling layout. Its text does not need to participate in cross-message selection for v1, but it must remain in reading order and scroll with the conversation.

### 8. Mac Catalyst behavior

Choose and document one of these before merging the new Desktop reference:

- Preferred: a single native selectable attributed-text view with the same document projection and reduced visual formatting.
- Acceptable first increment: a MAUI fallback matching the current bubbles but explicitly documenting that cross-message selection is Windows-only.

The fallback must compile and preserve bindings even if feature parity is deferred.

## Delivery phases

### Phase 0 — feasibility spike

Build a throwaway Windows prototype using `messages.json` with at least three messages and representative Markdown. Prove all of the following before building the full component:

1. Drag selection crosses assistant, user, and tool message boundaries.
2. Ctrl+C produces the expected canonical text.
3. Bubble-like left/right visual regions can coexist with the one text owner.
4. A final assistant region can be updated repeatedly without losing selection in earlier messages.
5. Selection offsets can be captured/restored, or `RichEditBox` is selected instead.

Record the control choice and any visual concessions in this document. This spike is the main go/no-go gate.

### Phase 1 — library skeleton and contracts

- Add the Chat project and solution/Desktop references.
- Add the presentation interface and implement it on `MessageViewModel`.
- Add `ChatTranscriptView`, handler registration, bindable properties, and lifecycle cleanup.
- Add a minimal Mac Catalyst compile-safe renderer/fallback.

### Phase 2 — document projection

- Implement Markdig-to-document conversion for all Markdown currently supported by `MarkdownMessageView`.
- Define canonical plain text and UTF-16 offset mapping.
- Add message/tool projection and cache invalidation.
- Compare representative output with the existing renderer.

### Phase 3 — Windows transcript renderer

- Render one selectable native document.
- Add role alignment, bubble width/fill, spacing, timestamp presentation, copy affordances, tool controls, empty state, and scroll behavior.
- Implement theme palette updates and accessibility metadata.

### Phase 4 — streaming and integration

- Implement collection/item synchronization and frame-coalesced streaming patches.
- Preserve selection and viewport; implement conditional auto-follow.
- Replace only the `CollectionView` transcript in `ChatPanelView.xaml`.
- Move its approval footer into the new component's footer slot.
- Remove obsolete `MessagesView.ScrollTo` and per-bubble copy handlers only after the new control owns those behaviors.

### Phase 5 — hardening

- Exercise long histories, rapid deltas, cancellation, errors, thread switches, resize, DPI, and theme changes.
- Resolve accessibility and clipboard output issues.
- Decide whether old `MarkdownMessageView` remains useful elsewhere or can be retired in a separate cleanup.

## Validation checklist

### Automated component tests

Although implementation tests are a separate decision, the component should be considered complete only when these behaviors are covered:

- Markdown projection for every supported block/inline type, malformed input, nested structures, Unicode, emoji, and CRLF/LF input.
- Canonical plain text and UTF-16 message/block offsets.
- Collection Add/Remove/Replace/Move/Reset and `ItemsSource` replacement.
- Item `Content`, tool payload, and expansion notifications.
- Subscription detachment after item removal, binding replacement, and control disposal.
- Coalescing of rapid streamed deltas.

### Windows UI/integration checks

- Load at least three Markdown messages; drag from the middle of message 1 through message 3; Ctrl+C must contain the expected ordered text.
- Repeat across headings, bold/italic, links, lists, code, tables, user/assistant boundaries, and an intervening tool message.
- Verify Shift+Arrow, Ctrl+Shift+Arrow, Ctrl+A, Ctrl+C, pointer drag, and context-menu copy.
- Keep a selection in an earlier message while deltas append to the last assistant message.
- Verify no forced scroll while selecting or reading older content; verify auto-follow at the bottom.
- Clear/reload a thread and confirm no duplicated subscriptions or stale blocks.
- Test light, dark, device-theme changes, high contrast, 100–200% DPI, narrow/wide panels, and keyboard-only navigation.
- Confirm approval buttons, tool expansion, and copy buttons remain reachable and do not break drag selection.
- Check UI Automation reading order and names.

### Performance checks

Use realistic long threads and rapid token streams. Record document projection time, render/patch time, allocations, memory retained by a full thread, and scroll responsiveness. Set a practical history limit or incremental loading strategy only if measurements require it; do not add virtualization that breaks the single selection owner.

### Build checks

```cmd
dotnet build src\Subconscious.Chat\Subconscious.Chat.csproj
dotnet build src\Subconscious.Desktop\Subconscious.Desktop.csproj
dotnet build Subconscious.slnx
```

Also evaluate/build the Mac Catalyst target on macOS before calling the integration cross-platform complete. Run Desktop UI automation explicitly if its project remains outside `Subconscious.slnx`.

## Risks and mitigations

- **Selection resets during streaming:** preserve logical offsets, patch only the changed region, and coalesce updates; move from `RichTextBlock` to `RichEditBox` if range restoration is insufficient.
- **Bubble visuals conflict with one text owner:** prioritize selection, use paragraph formatting or background adorners, and document visual differences rather than splitting text into controls.
- **No virtualization:** cache parsed messages, patch incrementally, measure long histories, and consider loading older messages on demand without removing currently selected content.
- **Tool/code/table controls split selection:** render their textual content inside the document and keep only actions outside it.
- **Theme mismatch:** explicitly translate semantic MAUI resources to native brushes on `ThemeRevision`.
- **Unicode offset bugs:** standardize on UTF-16 offsets for native ranges and test surrogate pairs, combining marks, and emoji.
- **Clipboard noise:** define canonical text separators and keep decorative controls/timestamps out of copied transcript text unless intentionally included.
- **Accessibility regression:** retain one logical reading order, add automation names to external action controls, and test keyboard focus and high contrast.
- **Project dependency cycle:** keep Chat presentation-only and have Desktop implement Chat's interface.
- **Mac build break:** include a compile-safe Mac target/renderer before adding the unconditional Desktop project reference.

## Recommended first decision

Start with the Phase 0 spike. The entire design depends on validating that one WinUI document can provide acceptable bubble-like formatting while preserving selection across streaming updates. Select `RichTextBlock` if it passes; otherwise standardize on a read-only, display-styled `RichEditBox`. Once that decision is made, the rest of the component can be built without risking a late architectural rewrite.
