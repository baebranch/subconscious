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
        FencedCodeBlock fencedCode => CreateCodeBlock(fencedCode.Lines.ToString()),
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

    private static View CreateCodeBlock(string code)
    {
        var label = CreateText(code);
        label.FontFamily = "monospace";
        label.LineBreakMode = LineBreakMode.NoWrap;
        var codeBorder = new Border
        {
            Padding = 8,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Content = new ScrollView { Orientation = ScrollOrientation.Both, Content = label },
        };
        codeBorder.SetDynamicResource(BackgroundColorProperty, "PanelBackgroundColor");
        return codeBorder;
    }

    private static View CreateTable(ContainerBlock table)
    {
        var rows = ChildrenOf(table).OfType<ContainerBlock>().ToList();
        if (rows.Count == 0)
        {
            return CreateText(ExtractText(table));
        }

        var columnCount = rows.Max(row => ChildrenOf(row).Count());
        var grid = new Grid { RowSpacing = 1, ColumnSpacing = 1 };
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

        return new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = grid };
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
                    target.Spans.Add(CreateSpan(current.ToString() ?? string.Empty, attributes, underline));
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
        _ => block.ToString() ?? string.Empty,
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
                _ => current.ToString(),
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
