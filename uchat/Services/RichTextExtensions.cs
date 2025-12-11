using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Windows.UI.Text;

namespace uchat;

public static class RichTextExtensions
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.RegisterAttached(
            "Markdown",
            typeof(string),
            typeof(RichTextExtensions),
            new PropertyMetadata(null, OnMarkdownChanged));

    public static void SetMarkdown(DependencyObject element, string value)
    {
        element.SetValue(MarkdownProperty, value);
    }

    public static string GetMarkdown(DependencyObject element)
    {
        return (string)element.GetValue(MarkdownProperty);
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
        {
            var text = e.NewValue as string ?? "";
            textBlock.Inlines.Clear();
            ParseAndAddFormattedText(textBlock, text);
            textBlock.InvalidateMeasure();
        }
    }

    private static void ParseAndAddFormattedText(TextBlock textBlock, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var formattingRegex = new Regex(
            @"(\*\*(?<bold>.+?)\*\*)|(\*(?<italic>[^*]+?)\*)|(~~(?<strike>.+?)~~)",
            RegexOptions.Singleline);
        var matches = formattingRegex.Matches(text);
        int lastIndex = 0;
        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                string plainText = text.Substring(lastIndex, match.Index - lastIndex);
                textBlock.Inlines.Add(new Run { Text = plainText });
            }
            if (match.Groups["bold"].Success)
            {
                textBlock.Inlines.Add(new Run
                {
                    Text = match.Groups["bold"].Value,
                    FontWeight = FontWeights.Bold
                });
            }
            else if (match.Groups["italic"].Success)
            {
                textBlock.Inlines.Add(new Run
                {
                    Text = match.Groups["italic"].Value,
                    FontStyle = FontStyle.Italic
                });
            }
            else if (match.Groups["strike"].Success)
            {
                textBlock.Inlines.Add(new Run
                {
                    Text = match.Groups["strike"].Value,
                    TextDecorations = TextDecorations.Strikethrough
                });
            }

            lastIndex = match.Index + match.Length;
        }
        if (lastIndex < text.Length)
        {
            textBlock.Inlines.Add(new Run { Text = text.Substring(lastIndex) });
        }
    }
}