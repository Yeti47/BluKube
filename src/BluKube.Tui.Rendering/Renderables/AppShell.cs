using Spectre.Console;
using Spectre.Console.Rendering;

namespace BluKube.Tui.Rendering;

/// <summary>
/// Outer chrome: header rule, padded body, footer rule, and hint line.
/// </summary>
internal sealed class AppShell(IRenderable body, string footer) : IRenderable
{
    private IRenderable Build()
    {
        var grid = new Grid().AddColumn();
        grid.AddRow(new Rule("[bold blue]BluKube[/]").LeftJustified());
        grid.AddRow(new Padder(body, new Padding(0, 1)));
        grid.AddRow(new Rule().LeftJustified());
        grid.AddRow(new Markup(Markup.Escape(footer)));
        return grid;
    }

    public Measurement Measure(RenderOptions options, int maxWidth) =>
        Build().Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
        Build().Render(options, maxWidth);
}
