namespace BluKube.Contracts;

/// <summary>
/// Wire-level audio format used by the SessionHub <c>StreamAudio</c> stream.
/// Each item in the stream is a single Opus packet whose decoded PCM matches
/// these parameters. Server and client must agree on these constants.
/// </summary>
public static class AudioFormat
{
    /// <summary>Opus sample rate (Hz).</summary>
    public const int SampleRate = 48_000;

    /// <summary>Channel count (stereo).</summary>
    public const int Channels = 2;

    /// <summary>Frame duration in milliseconds.</summary>
    public const int FrameMilliseconds = 20;

    /// <summary>Samples per channel per Opus frame (<see cref="SampleRate"/> * <see cref="FrameMilliseconds"/> / 1000).</summary>
    public const int SamplesPerFrame = SampleRate * FrameMilliseconds / 1000;

    /// <summary>Bytes per Opus frame of decoded interleaved s16 PCM.</summary>
    public const int PcmBytesPerFrame = SamplesPerFrame * Channels * sizeof(short);

    /// <summary>Target Opus encoder bitrate (bits per second).</summary>
    public const int Bitrate = 96_000;
}
