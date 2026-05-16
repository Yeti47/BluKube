using System.Threading.Channels;

namespace BluKube.Tui.Rendering;

/// <summary>
/// Handles key input and server calls for the Playback view mode.
/// </summary>
internal sealed class PlaybackViewController(BluKubeConnection connection) : IView
{
    public ViewMode Mode => ViewMode.Player;
    
    public async Task DispatchAsync(KeyPress key, UiState state, Channel<bool> redraw, CancellationToken ct)
    {
        if (key.IsAltCharacter('c'))
        {
            state.CompactPlayback = !state.CompactPlayback;
            await redraw.Writer.WriteAsync(true, ct);
            return;
        }

        if (key.Key == Key.Escape)
        {
            await connection.StopAsync(ct);
            state.Mode = ViewMode.Search;
            state.Error = null;
            await redraw.Writer.WriteAsync(true, ct);
            return;
        }

        if (state.ServerState is not PlaybackState playback) return;

        switch (key.Key)
        {
            case Key.Space:
                // Optimistic UI: flip the playing flag immediately, then confirm
                // with the server response.
                state.ServerState = playback with { IsPlaying = !playback.IsPlaying };
                await redraw.Writer.WriteAsync(true, ct);
                state.ServerState = playback.IsPlaying
                    ? await connection.PauseAsync(ct)
                    : await connection.ResumeAsync(ct);
                await redraw.Writer.WriteAsync(true, ct);
                break;
            case Key.UpArrow:
                await SetVolumeAsync(state, playback.Volume + 0.05f, redraw, ct);
                break;
            case Key.DownArrow:
                await SetVolumeAsync(state, playback.Volume - 0.05f, redraw, ct);
                break;
            case Key.LeftArrow:
                await SeekAsync(state, playback, -TimeSpan.FromSeconds(key.Shift ? 30 : 10), redraw, ct);
                break;
            case Key.RightArrow:
                await SeekAsync(state, playback, TimeSpan.FromSeconds(key.Shift ? 30 : 10), redraw, ct);
                break;
        }
    }

    private async Task SetVolumeAsync(UiState state, float volume, Channel<bool> redraw, CancellationToken ct)
    {
        state.ServerState = await connection.SetVolumeAsync(Math.Clamp(volume, 0f, 1f), ct);
        await redraw.Writer.WriteAsync(true, ct);
    }

    private async Task SeekAsync(
        UiState state,
        PlaybackState playback,
        TimeSpan delta,
        Channel<bool> redraw,
        CancellationToken ct)
    {
        var next = playback.Position + delta;
        if (next < TimeSpan.Zero) next = TimeSpan.Zero;
        if (playback.Duration > TimeSpan.Zero && next > playback.Duration) next = playback.Duration;

        state.ServerState = await connection.SeekToAsync(next, ct);
        await redraw.Writer.WriteAsync(true, ct);
    }
}
