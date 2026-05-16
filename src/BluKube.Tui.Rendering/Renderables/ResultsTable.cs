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
    string? pageInfo = null
) : IRenderable
{
    private IRenderable Build()
    {
        if (items.Count == 0)
            return new Panel(new Markup("[grey]No results.[/]")).Header("[bold]results[/]");

        // Width(1) pins the selector column so it never collapses to zero
        // when all selector cells are empty (e.g. during loading).
        var table = new Table()
            .Border(TableBorder.Rounded)
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
                    new Markup(string.Empty),
                    i == midRow
                        ? new Markup("[bold white on blue] Loading... [/]")
                        : new SanitizedText(
                            item.Title,
                            maxLength: 60,
                            style: SanitizedTextStyle.Dim
                        ),
                    new SanitizedText(item.Channel, style: SanitizedTextStyle.Dim),
                    new DurationLabel(item.Duration, dim: true)
                );
            }
        }
        else
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var selected = i == selectedIndex;
                table.AddRow(
                    new Markup(selected ? "[blue]>[/]" : string.Empty),
                    new SanitizedText(
                        item.Title,
                        maxLength: 60,
                        style: selected ? SanitizedTextStyle.Selected : SanitizedTextStyle.Plain
                    ),
                    new SanitizedText(
                        item.Channel,
                        style: selected ? SanitizedTextStyle.Selected : SanitizedTextStyle.Plain
                    ),
                    new DurationLabel(item.Duration, selected)
                );
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
