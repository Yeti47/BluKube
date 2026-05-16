using Spectre.Console;
using Spectre.Console.Rendering;

namespace BluKube.Tui.Rendering;

internal sealed class TimeRangeLabel(TimeSpan position, TimeSpan duration) : IRenderable
{
    private IRenderable Build()
    {
        var text = $"{DurationLabel.Format(position)} / {DurationLabel.Format(duration)}";
        return new Markup(Markup.Escape(text));
    }

    public Measurement Measure(RenderOptions options, int maxWidth) =>
        Build().Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
        Build().Render(options, maxWidth);
}
