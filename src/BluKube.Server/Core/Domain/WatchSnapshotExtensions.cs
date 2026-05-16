using BluKube.Server.Core.Playback;

namespace BluKube.Server.Core.Domain;

internal static class WatchSnapshotExtensions
{
    internal static PlayerSnapshot ToPlayerSnapshot(this WatchSnapshot s, string videoId) =>
        new(videoId, s.Position, s.Duration, s.IsPlaying, s.Volume);
}
