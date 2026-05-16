using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace BluKube.Tui.Rendering;

internal enum SanitizedTextStyle
{
    Plain,
    Dim,
    Selected
}

internal sealed class SanitizedText(
    string? value,
    int? maxLength = null,
    SanitizedTextStyle style = SanitizedTextStyle.Plain,
    string? fallback = null) : IRenderable
{
    private IRenderable Build()
    {
        var text = Sanitize(value);
        if (string.IsNullOrWhiteSpace(text) && fallback is not null)
            text = Sanitize(fallback);

        text = Truncate(text, maxLength);
        var escaped = Markup.Escape(text);

        return style switch
        {
            SanitizedTextStyle.Dim => new Markup($"[grey]{escaped}[/]"),
            SanitizedTextStyle.Selected => new Markup($"[bold blue]{escaped}[/]"),
            _ => new Markup(escaped)
        };
    }

    public Measurement Measure(RenderOptions options, int maxWidth) =>
        Build().Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
        Build().Render(options, maxWidth);

    private static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsTerminalSpacingRune(rune) || ShouldStripTerminalRune(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString().Trim();
    }

    private static string Truncate(string text, int? maxLength)
    {
        if (maxLength is not { } limit || text.EnumerateRunes().Count() <= limit)
            return text;

        if (limit <= 0)
            return string.Empty;

        if (limit == 1)
            return "\u2026";

        var builder = new StringBuilder(limit);
        foreach (var rune in text.EnumerateRunes().Take(limit - 1))
            builder.Append(rune.ToString());

        builder.Append('\u2026');
        return builder.ToString();
    }

    private static bool ShouldStripTerminalRune(Rune rune)
    {
        if (rune.Value is 0x200D or 0x20E3 or 0xFE0E or 0xFE0F)
            return true;

        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.OtherSymbol
            or UnicodeCategory.ModifierSymbol;
    }

    private static bool IsTerminalSpacingRune(Rune rune)
    {
        if (rune.Value <= char.MaxValue && char.IsWhiteSpace((char)rune.Value))
            return true;

        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.SpaceSeparator
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator
            or UnicodeCategory.Control;
    }
}
