# Subconscious.WYSIWYG

Reusable .NET MAUI file editing surfaces used by `Subconscious.Desktop`.

## Components

- `EditorWorkspaceView`: closeable, thin-border tabs and one active surface.
- `MarkdownEditorView`: rendered, single-panel WYSIWYG Markdown with formatting controls.
- `CodeEditorView`: selectable/editable monospaced code, line numbers, syntax highlighting, and brace folding.
- Native MAUI `Editor`: plain text files.
- `EditorTheme`: host-configurable surface, text, accent, selection, divider, and syntax colours.

The host supplies `IEditorDocument` instances and owns persistence. Every native editor change carries the originating document ID, preventing an inactive tab from overwriting the selected file.

## Host setup

Call `UseSubconsciousWysiwyg()` while building the MAUI app, reference `EditorWorkspaceView`, and bind `ItemsSource`, `SelectedDocument`, `CloseCommand`, and `Theme`. Desktop maps its persisted Device/Light/Dark and accent palette into `EditorTheme`.

`Samples/FileEditor` contains Markdown, C#, JSON, and text fixtures. The C# fixture is excluded from compilation.

## Binary viewers

PDF and Office/OpenDocument files should remain read-only. A host should provide an allow-list-validated stream from its backend, then attach a local native PDF renderer or a sandboxed Office-to-HTML/page-image renderer. Do not upload workspace documents to external conversion services by default. Editing these formats remains intentionally outside this component.
