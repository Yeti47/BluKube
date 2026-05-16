using Spectre.Console;
using Spectre.Console.Rendering;

namespace BluKube.Tui.Rendering;

internal sealed class DurationLabel(
    TimeSpan duration,
    bool selected = false,
    bool dim = false) : IRenderable
{
    private IRenderable Build()
    {
        if (IsLive)
            return new Markup("[bold white on red] LIVE [/]");

        var text = Markup.Escape(Format(duration));
        if (dim)
            return new Markup($"[grey]{text}[/]");

        return selected
            ? new Markup($"[bold blue]{text}[/]")
            : new Markup(text);
    }

    public bool IsLive => duration <= TimeSpan.Zero;

    public Measurement Measure(RenderOptions options, int maxWidth) =>
        Build().Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
        Build().Render(options, maxWidth);

    internal static string Format(TimeSpan time) =>
        time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
}
