namespace BluKube.Server.Core.Domain;

/// <summary>
/// A single playback engine. Owns whatever resources are needed to play
/// one stream at a time. Commands return the resulting <see cref="PlayerSnapshot"/>;
/// background changes (position ticks, failures) flow through <see cref="Events"/>.
/// </summary>
public interface IMediaPlayer : IAsyncDisposable
{
    Task<PlayerSnapshot> PlayAsync(string videoId, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task<PlayerSnapshot> PauseAsync(CancellationToken ct);
    Task<PlayerSnapshot> ResumeAsync(CancellationToken ct);
    Task<PlayerSnapshot> SeekToAsync(TimeSpan position, CancellationToken ct);
    Task<PlayerSnapshot> SetVolumeAsync(float volume, CancellationToken ct);

    IAsyncEnumerable<PlaybackEvent> Events(CancellationToken ct);
}

public sealed record PlayerSnapshot(
    string VideoId,
    TimeSpan Position,
    TimeSpan Duration,
    bool IsPlaying,
    float Volume
);
