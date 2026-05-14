using System.Threading.Channels;

namespace BluKube.Tui.Rendering;

internal interface IView
{
    ViewMode Mode { get; }
    Task DispatchAsync(KeyPress key, UiState state, Channel<bool> redraw, CancellationToken ct);
}
