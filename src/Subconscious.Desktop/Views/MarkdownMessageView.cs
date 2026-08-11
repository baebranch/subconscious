using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Maui.Controls.Shapes;

namespace Subconscious.Desktop.Views;

/// <summary>Renders assistant and user Markdown into native, theme-aware MAUI controls.</summary>
public sealed class MarkdownMessageView : ContentView
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static readonly BindableProperty MarkdownProperty = BindableProperty.Create(
        nameof(Markdown), typeof(string), typeof(MarkdownMessageView), string.Empty,
        propertyChanged: static (view, _, _) => ((MarkdownMessageView)view).Render());

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MarkdownMessageView() => Render();

    private void Render()
    {
        var content = new VerticalStackLayout { Spacing = 6 };
        var document = Markdig.Markdown.Parse(Markdown ?? string.Empty, Pipeline);
        foreach (var block in document)
        {
            content.Children.Add(CreateBlock(block));
        }

        Content = content;
    }

    private static View CreateBlock(Block block) => block switch
    {
        HeadingBlock heading => CreateHeading(heading),
        ParagraphBlock paragraph => CreateParagraph(paragraph),
        FencedCodeBlock fencedCode => CreateFencedCodeBlock(fencedCode),
        CodeBlock code => CreateCodeBlock(code.Lines.ToString()),
        QuoteBlock quote => CreateQuote(quote),
        ListBlock list => CreateList(list),
        ThematicBreakBlock => CreateDivider(),
        ContainerBlock container when block.GetType().Name == "Table" => CreateTable(container),
        ContainerBlock container => CreateContainer(container),
        _ => CreateText(ExtractText(block)),
    };

    private static Label CreateHeading(HeadingBlock heading)
    {
        var label = CreateInlineLabel(heading.Inline);
        label.FontAttributes = FontAttributes.Bold;
        label.FontSize = heading.Level switch { 1 => 20, 2 => 18, 3 => 16, _ => 14 };
        return label;
    }

    private static Label CreateParagraph(ParagraphBlock paragraph) => CreateInlineLabel(paragraph.Inline);

    private static Border CreateQuote(QuoteBlock quote)
    {
        var border = new Border
        {
            Padding = new Thickness(8, 4),
            StrokeThickness = 3,
            StrokeShape = new RoundRectangle { CornerRadius = 3 },
            Content = CreateContainer(quote),
        };
        border.SetDynamicResource(Border.StrokeProperty, "AccentBrush");
        return border;
    }

    private static View CreateList(ListBlock list)
    {
        var layout = new VerticalStackLayout { Spacing = 4 };
        var number = 1;
        foreach (var item in ChildrenOf(list).OfType<ListItemBlock>())
        {
            var row = new HorizontalStackLayout { Spacing = 6 };
            var marker = CreateText(list.IsOrdered ? $"{number++}." : "•");
            marker.WidthRequest = 24;
            marker.HorizontalTextAlignment = TextAlignment.End;
            row.Children.Add(marker);
            row.Children.Add(CreateContainer(item));
            layout.Children.Add(row);
        }

        return layout;
    }

    private static View CreateContainer(ContainerBlock container)
    {
        var layout = new VerticalStackLayout { Spacing = 4 };
        foreach (var child in ChildrenOf(container))
        {
            layout.Children.Add(CreateBlock(child));
        }

        return layout;
    }

    private static View CreateFencedCodeBlock(FencedCodeBlock fencedCode)
    {
        var info = fencedCode.Info?.ToString()?.Trim() ?? string.Empty;
        var language = info.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return CreateCodeBlockFrame(fencedCode.Lines.ToString(), language, showHeader: true);
    }

    private static View CreateCodeBlock(string code) =>
        CreateCodeBlockFrame(code, CodeSyntaxHighlighter.GuessLanguage(code), showHeader: true);

    private static View CreateCodeBlockFrame(string code, string? language, bool showHeader)
    {
        var label = CodeSyntaxHighlighter.CreateLabel(code, language);

        var codeBody = new Border
        {
            Padding = 8,
            StrokeThickness = 0,
            Content = new ScrollView { Orientation = ScrollOrientation.Both, Content = label },
        };
        codeBody.SetDynamicResource(BackgroundColorProperty, "PanelBackgroundColor");

        if (!showHeader)
        {
            var plainFrame = new Border
            {
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 5 },
                Content = codeBody,
            };
            plainFrame.SetDynamicResource(Border.StrokeProperty, "DividerBrush");
            return plainFrame;
        }

        var languageLabel = CreateText(string.IsNullOrWhiteSpace(language) ? "Code" : language);
        languageLabel.FontFamily = "monospace";
        languageLabel.FontSize = 11;
        languageLabel.VerticalOptions = LayoutOptions.Center;
        languageLabel.SetDynamicResource(Label.TextColorProperty, "SecondaryTextColor");

        var copyButton = CreateCodeCopyButton(code);

        var header = new Grid { Padding = new Thickness(8, 4), ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.SetDynamicResource(BackgroundColorProperty, "HoverColor");
        header.Children.Add(languageLabel);
        header.Children.Add(copyButton);
        Grid.SetColumn(copyButton, 1);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.Children.Add(header);
        var divider = CreateDivider();
        layout.Children.Add(divider);
        Grid.SetRow(divider, 1);
        layout.Children.Add(codeBody);
        Grid.SetRow(codeBody, 2);

        var frame = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 5 },
            Content = layout,
        };
        frame.SetDynamicResource(Border.StrokeProperty, "DividerBrush");
        return frame;
    }

    private static Border CreateCodeCopyButton(string code)
    {
        var icon = new Label
        {
            Text = "\uE8C8",
            FontFamily = "Segoe Fluent Icons",
            FontSize = 15,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            InputTransparent = true,
        };
        icon.SetDynamicResource(Label.TextColorProperty, "SecondaryTextColor");

        var copyButton = new Border { Content = icon };
        if (Application.Current?.Resources.TryGetValue("BubbleCopyButton", out var style) == true && style is Style borderStyle)
        {
            copyButton.Style = borderStyle;
        }
        else
        {
            copyButton.WidthRequest = 24;
            copyButton.HeightRequest = 24;
            copyButton.Padding = 4;
            copyButton.StrokeThickness = 0;
            copyButton.StrokeShape = new RoundRectangle { CornerRadius = 4 };
        }

        SemanticProperties.SetDescription(copyButton, "Copy code");
        ToolTipProperties.SetText(copyButton, "Copy code");
        copyButton.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await Clipboard.Default.SetTextAsync(code)),
        });
        return copyButton;
    }

    private static View CreateTable(ContainerBlock table)
    {
        var rows = ChildrenOf(table).OfType<ContainerBlock>().ToList();
        if (rows.Count == 0)
        {
            return CreateText(ExtractText(table));
        }

        // The DividerColor grid background is exposed through the 1px row/column gaps, forming
        // stable native table rules without double-stroking shared cell edges.
        var columnCount = rows.Max(row => ChildrenOf(row).Count());
        var grid = new Grid { RowSpacing = 1, ColumnSpacing = 1 };
        grid.SetDynamicResource(BackgroundColorProperty, "DividerColor");
        for (var column = 0; column < columnCount; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cells = ChildrenOf(rows[rowIndex]).ToList();
            for (var column = 0; column < cells.Count; column++)
            {
                var text = CreateText(ExtractText(cells[column]));
                text.LineBreakMode = LineBreakMode.NoWrap;
                text.FontAttributes = rowIndex == 0 ? FontAttributes.Bold : FontAttributes.None;
                var cell = new Border
                {
                    Padding = 6,
                    StrokeThickness = 0,
                    Content = text,
                };
                cell.SetDynamicResource(BackgroundColorProperty, rowIndex == 0 ? "HoverColor" : "PanelBackgroundColor");
                grid.Children.Add(cell);
                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, column);
            }
        }

        var frame = new Border
        {
            Padding = 1,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 5 },
            Content = grid,
        };
        frame.SetDynamicResource(Border.StrokeProperty, "DividerBrush");
        frame.SetDynamicResource(BackgroundColorProperty, "DividerColor");
        return new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = frame };
    }

    private static BoxView CreateDivider()
    {
        var divider = new BoxView { HeightRequest = 1 };
        divider.SetDynamicResource(BoxView.ColorProperty, "DividerColor");
        return divider;
    }

    private static Label CreateInlineLabel(ContainerInline? inline)
    {
        var label = CreateText(string.Empty);
        var formatted = new FormattedString();
        AppendInlines(inline?.FirstChild, formatted, FontAttributes.None, false);
        label.FormattedText = formatted;
        return label;
    }

    private static void AppendInlines(Inline? inline, FormattedString target, FontAttributes attributes, bool underline)
    {
        for (var current = inline; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    target.Spans.Add(CreateSpan(literal.Content.ToString(), attributes, underline));
                    break;
                case LineBreakInline:
                    target.Spans.Add(CreateSpan(Environment.NewLine, attributes, underline));
                    break;
                case CodeInline code:
                    var codeSpan = CreateSpan(code.Content, attributes, underline);
                    codeSpan.FontFamily = "monospace";
                    target.Spans.Add(codeSpan);
                    break;
                case EmphasisInline emphasis:
                    var emphasisAttributes = attributes;
                    if (emphasis.DelimiterCount >= 2)
                    {
                        emphasisAttributes |= FontAttributes.Bold;
                    }
                    else
                    {
                        emphasisAttributes |= FontAttributes.Italic;
                    }
                    AppendInlines(emphasis.FirstChild, target, emphasisAttributes, underline);
                    break;
                case LinkInline link:
                    AppendInlines(link.FirstChild, target, attributes, true);
                    break;
                case ContainerInline container:
                    AppendInlines(container.FirstChild, target, attributes, underline);
                    break;
                default:
                    // Extension bookkeeping nodes (for example auto-identifier references) are
                    // parser metadata, not visible document text.
                    break;
            }
        }
    }

    private static Span CreateSpan(string text, FontAttributes attributes, bool underline) => new()
    {
        Text = text,
        FontAttributes = attributes,
        TextDecorations = underline ? TextDecorations.Underline : TextDecorations.None,
    };

    private static Label CreateText(string text) => new Label
    {
        Text = text,
        LineBreakMode = LineBreakMode.WordWrap,
    }.WithThemeTextColor();

    private static IEnumerable<Block> ChildrenOf(ContainerBlock container)
    {
        foreach (var child in container)
        {
            yield return child;
        }
    }

    private static string ExtractText(Block block) => block switch
    {
        ParagraphBlock paragraph => ExtractInlineText(paragraph.Inline?.FirstChild),
        CodeBlock code => code.Lines.ToString(),
        ContainerBlock container => string.Join(Environment.NewLine, ChildrenOf(container).Select(ExtractText)),
        _ => string.Empty,
    };

    private static string ExtractInlineText(Inline? inline)
    {
        var text = new System.Text.StringBuilder();
        for (var current = inline; current is not null; current = current.NextSibling)
        {
            text.Append(current switch
            {
                LiteralInline literal => literal.Content.ToString(),
                LineBreakInline => Environment.NewLine,
                CodeInline code => code.Content,
                ContainerInline container => ExtractInlineText(container.FirstChild),
                _ => string.Empty,
            });
        }
        return text.ToString();
    }
}

internal static class MarkdownViewExtensions
{
    public static Label WithThemeTextColor(this Label label)
    {
        label.SetDynamicResource(Label.TextColorProperty, "PrimaryTextColor");
        return label;
    }
}
