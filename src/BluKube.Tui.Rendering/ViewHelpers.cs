using Spectre.Console;

namespace BluKube.Tui.Rendering;

internal static class ViewHelpers
{
    public static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";

    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "\u2026";

    public static string FormatCell(string value, bool selected)
    {
        var escaped = Markup.Escape(value);
        return selected ? $"[bold blue]{escaped}[/]" : escaped;
    }

    public static bool TryGetInputCharacter(KeyPress key, out char character)
    {
        if (key.Key is Key.Char or Key.Q && !char.IsControl(key.Character) && key.Character != '\0')
        {
            character = key.Character;
            return true;
        }

        character = default;
        return false;
    }

    public static string ExtractVideoId(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in query)
            {
                var pair = part.Split('=', 2);
                if (pair.Length == 2 && pair[0] == "v")
                    return Uri.UnescapeDataString(pair[1]);
            }

            var segment = uri.Segments.LastOrDefault();
            if (!string.IsNullOrEmpty(segment)) return segment.TrimEnd('/');
        }

        return url;
    }
}
