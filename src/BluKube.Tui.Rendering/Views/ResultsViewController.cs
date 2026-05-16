using System.Threading.Channels;

namespace BluKube.Tui.Rendering;

/// <summary>
/// Handles key input and server calls for the Results view mode.
/// </summary>
internal sealed class ResultsViewController(BluKubeConnection connection) : IView
{
    public ViewMode Mode => ViewMode.Results;

    public async Task DispatchAsync(
        KeyPress key,
        UiState state,
        Channel<bool> redraw,
        CancellationToken ct
    )
    {
        switch (key.Key)
        {
            case Key.Escape:
            case Key.Backspace:
                state.Mode = ViewMode.Search;
                state.Error = null;
                await redraw.Writer.WriteAsync(true, ct);
                break;
            case Key.UpArrow:
                if (state.Results.Count > 0)
                {
                    var pageMin = state.Page * state.PageSize;
                    state.SelectedIndex = Math.Max(pageMin, state.SelectedIndex - 1);
                    await redraw.Writer.WriteAsync(true, ct);
                }
                break;
            case Key.DownArrow:
                if (state.Results.Count > 0)
                {
                    var pageMax = Math.Min(
                        state.Results.Count - 1,
                        (state.Page + 1) * state.PageSize - 1
                    );
                    state.SelectedIndex = Math.Min(pageMax, state.SelectedIndex + 1);
                    await redraw.Writer.WriteAsync(true, ct);
                }
                break;
            case Key.LeftArrow:
                if (state.Page > 0)
                {
                    state.Page--;
                    state.SelectedIndex = state.Page * state.PageSize;
                    await redraw.Writer.WriteAsync(true, ct);
                }
                break;
            case Key.RightArrow:
            {
                var totalPages = (state.Results.Count + state.PageSize - 1) / state.PageSize;
                if (state.Page < totalPages - 1)
                {
                    state.Page++;
                    state.SelectedIndex = state.Page * state.PageSize;
                    await redraw.Writer.WriteAsync(true, ct);
                }
                break;
            }
            case Key.Enter when state.Results.Count > 0:
                await PlaySelectedAsync(state, redraw, ct);
                break;
            default:
                if (key.TryGetInputCharacter(out var character))
                {
                    state.Mode = ViewMode.Search;
                    state.Query = character.ToString();
                    state.Error = null;
                    await redraw.Writer.WriteAsync(true, ct);
                }
                break;
        }
    }

    private async Task PlaySelectedAsync(UiState state, Channel<bool> redraw, CancellationToken ct)
    {
        var item = state.Results[state.SelectedIndex];
        var videoId = ExtractVideoId(item.Url);

        state.IsBusy = true;
        state.Error = null;
        await redraw.Writer.WriteAsync(true, ct);

        var response = await connection.PlayAsync(videoId, ct);
        state.ServerState = response;
        state.IsBusy = false;

        if (response is ErrorState error)
        {
            state.Error = error.Message;
            state.Status = null;
            await redraw.Writer.WriteAsync(true, ct);
            return;
        }

        state.CurrentTitle = item.Title;
        state.CurrentChannel = item.Channel;
        state.Mode = ViewMode.Player;
        state.Status = null;
        await redraw.Writer.WriteAsync(true, ct);
    }

    private static string ExtractVideoId(string url)
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
            if (!string.IsNullOrEmpty(segment))
                return segment.TrimEnd('/');
        }

        return url;
    }
}
