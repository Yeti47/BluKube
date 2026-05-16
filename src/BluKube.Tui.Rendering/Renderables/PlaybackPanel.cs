using Spectre.Console;
using Spectre.Console.Rendering;

namespace BluKube.Tui.Rendering;

/// <summary>
/// Playback info panel: metadata grid, volume row, and a progress bar,
/// with blank separator rows between each section.
/// </summary>
internal sealed class PlaybackPanel(
    PlaybackState? playback,
    string? title,
    string? channel,
    string? error,
    bool compact = false) : IRenderable
{
    private IRenderable Build()
    {
        if (playback is null)
            return new Panel(new Markup("[grey]Loading...[/]")).Header("[bold]playback[/]");

        if (compact)
            return BuildCompact(playback);

        var percent = playback.Duration.TotalSeconds > 0
            ? Math.Clamp(playback.Position.TotalSeconds / playback.Duration.TotalSeconds, 0d, 1d)
            : 0d;

        var progressBar = new BreakdownChart()
            .Width(60)
            .HideTags()
            .AddItem("done", percent * 100, Color.Purple)
            .AddItem("left", (1 - percent) * 100, Color.Grey);

        var meta = new Grid().AddColumn().AddColumn()
            .AddRow(new Markup("[grey]title[/]"), new SanitizedText(title, fallback: playback.VideoId))
            .AddRow(new Markup("[grey]channel[/]"), new SanitizedText(channel))
            .AddRow(new Markup("[grey]video[/]"), new Markup(Markup.Escape(playback.VideoId)))
            .AddRow(new Markup("[grey]state[/]"), new Markup(playback.IsPlaying ? "[purple]playing[/]" : "[yellow]paused[/]"))
            .AddRow(new Markup("[grey]time[/]"), new TimeRangeLabel(playback.Position, playback.Duration));

        var volumeRow = new Grid().AddColumn().AddColumn()
            .AddRow(new Markup("[grey]volume[/]"), new Markup($"{(int)Math.Round(playback.Volume * 100)}%"));

        var stack = new Grid().AddColumn();
        stack.AddRow(meta);
        stack.AddRow(new Markup(string.Empty)); // separator
        stack.AddRow(volumeRow);
        stack.AddRow(new Markup(string.Empty)); // separator
        stack.AddRow(progressBar);

        if (!string.IsNullOrWhiteSpace(error))
            stack.AddRow(new Markup($"[red]{Markup.Escape(error)}[/]"));

        return new Panel(stack).Header("[bold]playback[/]");
    }

    private IRenderable BuildCompact(PlaybackState playback)
    {
        var stack = new Grid().AddColumn().AddColumn()
            .AddRow(new Markup("[grey]state[/]"), new Markup(playback.IsPlaying ? "[purple]playing[/]" : "[yellow]paused[/]"))
            .AddRow(new Markup("[grey]time[/]"), new TimeRangeLabel(playback.Position, playback.Duration))
            .AddRow(new Markup("[grey]volume[/]"), new Markup($"{(int)Math.Round(playback.Volume * 100)}%"));

        if (!string.IsNullOrWhiteSpace(error))
            stack.AddRow(new Markup("[grey]error[/]"), new Markup($"[red]{Markup.Escape(error)}[/]"));

        return new Panel(stack).Header("[bold]playback[/]");
    }

    public Measurement Measure(RenderOptions options, int maxWidth) =>
        Build().Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
        Build().Render(options, maxWidth);
}
