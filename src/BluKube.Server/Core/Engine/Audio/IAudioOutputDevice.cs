namespace BluKube.Server.Core.Engine.Audio;

/// <summary>
/// A per-session audio output device. Owns whatever OS-level resource
/// the browser writes audio into (e.g. a PulseAudio null sink) and lets
/// callers pull encoded Opus packets from its monitor.
/// </summary>
public interface IAudioOutputDevice : IAsyncDisposable
{
    /// <summary>
    /// Browser-side env var hint (e.g. <c>PULSE_SINK</c> value). Empty when
    /// no env override is required (e.g. test fakes).
    /// </summary>
    string SinkName { get; }

    /// <summary>
    /// Yields one Opus packet per call. Each packet decodes to
    /// <see cref="BluKube.Contracts.AudioFormat.SamplesPerFrame"/> samples
    /// per channel of interleaved s16 PCM.
    /// </summary>
    IAsyncEnumerable<byte[]> StreamOpusAsync(CancellationToken ct);
}
