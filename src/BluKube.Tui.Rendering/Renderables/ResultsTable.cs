using Spectre.Console;
using Spectre.Console.Rendering;

namespace BluKube.Tui.Rendering;

/// <summary>
/// Search results list. When <paramref name="isLoading"/> is true, all rows
/// stay in place (preserving column widths) but are dimmed, and the middle
/// row shows a loading badge to simulate an overlay.
/// </summary>
internal sealed class ResultsTable(
    IReadOnlyList<MediaItem> items,
    int selectedIndex,
    bool isLoading,
    string query,
    string? pageInfo = null) : IRenderable
{
    private IRenderable Build()
    {
        if (items.Count == 0)
            return new Panel(new Markup("[grey]No results.[/]")).Header("[bold]results[/]");

        // Width(1) pins the selector column so it never collapses to zero
        // when all selector cells are empty (e.g. during loading).
        var table = new Table().Border(TableBorder.Rounded)
            .AddColumn(new TableColumn(string.Empty).Width(1))
            .AddColumn("Title")
            .AddColumn("Channel")
            .AddColumn("Length");

        if (isLoading)
        {
            var midRow = items.Count / 2;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                table.AddRow(
                    string.Empty,
                    i == midRow
                        ? "[bold blue on blue] Loading... [/]"
                        : $"[grey]{Markup.Escape(ViewHelpers.Truncate(item.Title, 60))}[/]",
                    $"[grey]{Markup.Escape(item.Channel)}[/]",
                    $"[grey]{ViewHelpers.FormatTime(item.Duration)}[/]");
            }
        }
        else
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var selected = i == selectedIndex;
                table.AddRow(
                    selected ? "[blue]>[/]" : string.Empty,
                    ViewHelpers.FormatCell(ViewHelpers.Truncate(item.Title, 60), selected),
                    ViewHelpers.FormatCell(item.Channel, selected),
                    ViewHelpers.FormatCell(ViewHelpers.FormatTime(item.Duration), selected));
            }
        }

        var header = pageInfo is not null
            ? $"[bold]results · {Markup.Escape(query)} · {pageInfo}[/]"
            : $"[bold]results · {Markup.Escape(query)}[/]";
        return new Panel(table).Header(header);
    }

    public Measurement Measure(RenderOptions options, int maxWidth) =>
        Build().Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
        Build().Render(options, maxWidth);
}
