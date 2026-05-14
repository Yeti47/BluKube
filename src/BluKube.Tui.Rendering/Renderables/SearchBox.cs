using Spectre.Console;
using Spectre.Console.Rendering;

namespace BluKube.Tui.Rendering;

/// <summary>
/// Search input panel. Fixed-width column keeps the box stable while the
/// user types, and a reserved status row prevents height changes when
/// "Searching..." appears.
/// </summary>
internal sealed class SearchBox(string query, string? status, string? error) : IRenderable
{
    private IRenderable Build()
    {
        var inputTable = new Table()
            .NoBorder()
            .HideHeaders()
            .AddColumn(new TableColumn(string.Empty).Width(40));

        var input = string.IsNullOrEmpty(query)
            ? "[grey]>[/] [grey]_[/]"
            : $"[grey]>[/] {Markup.Escape(query)}[grey]_[/]";
        inputTable.AddRow(new Markup(input));

        // Always render the status row so height stays constant.
        var statusText = !string.IsNullOrWhiteSpace(status)
            ? $"[grey]{Markup.Escape(status)}[/]"
            : " ";
        inputTable.AddRow(new Markup(statusText));

        if (!string.IsNullOrWhiteSpace(error))
            inputTable.AddRow(new Markup($"[red]{Markup.Escape(error)}[/]"));

        return new Panel(inputTable).Header("[bold]search[/]");
    }

    public Measurement Measure(RenderOptions options, int maxWidth) =>
        Build().Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
        Build().Render(options, maxWidth);
}
