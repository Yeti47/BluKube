using System.Threading.Channels;

namespace BluKube.Tui.Rendering;

/// <summary>
/// Handles key input and server calls for the Search view mode.
/// </summary>
internal sealed class SearchViewController(BluKubeConnection connection, int limit) : IView
{
    public ViewMode Mode => ViewMode.Search;

    public async Task DispatchAsync(KeyPress key, UiState state, Channel<bool> redraw, CancellationToken ct)
    {
        switch (key.Key)
        {
            case Key.Enter when !string.IsNullOrWhiteSpace(state.Query):
                await SearchAsync(state, redraw, ct);
                break;
            case Key.Backspace when state.Query.Length > 0:
                state.Query = state.Query[..^1];
                state.Error = null;
                await redraw.Writer.WriteAsync(true, ct);
                break;
            case Key.Escape:
                state.Query = string.Empty;
                state.Error = null;
                await redraw.Writer.WriteAsync(true, ct);
                break;
            case Key.Space:
                state.Query += ' ';
                await redraw.Writer.WriteAsync(true, ct);
                break;
            default:
                if (ViewHelpers.TryGetInputCharacter(key, out var character))
                {
                    state.Query += character;
                    state.Error = null;
                    await redraw.Writer.WriteAsync(true, ct);
                }
                break;
        }
    }

    private async Task SearchAsync(UiState state, Channel<bool> redraw, CancellationToken ct)
    {
        state.IsBusy = true;
        state.Error = null;
        state.Status = "Searching...";
        await redraw.Writer.WriteAsync(true, ct);

        var response = await connection.SearchAsync(state.Query.Trim(), limit, ct);
        state.ServerState = response;
        state.IsBusy = false;

        if (response is SearchResultsState results)
        {
            state.Results = results.Items;
            state.SelectedIndex = 0;
            state.Page = 0;
            state.Mode = ViewMode.Results;
            state.Status = results.Items.Count == 0 ? "No results." : null;
            state.Error = null;
        }
        else if (response is ErrorState error)
        {
            state.Error = error.Message;
            state.Status = null;
        }

        await redraw.Writer.WriteAsync(true, ct);
    }
}
