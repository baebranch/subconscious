# Standalone WYSIWYG debug host

This is an unpackaged, native MAUI test application for `Subconscious.WYSIWYG`. It has no reference to Desktop, Chat, or Engine projects and opens no Engine connection.

It provides writable code, Markdown, text, and generated stress-code fixtures. Use it to reproduce native `RichEditBox` focus, edit, folding, theme, and tab-switch behavior independently of the Desktop shell.

```cmd
dotnet run --project src\Subconscious.WYSIWYG.Debug\Subconscious.WYSIWYG.Debug.csproj
```

The **Restore fixtures** control repopulates closed tabs; **Reset selected** returns the current document to its initial content.
