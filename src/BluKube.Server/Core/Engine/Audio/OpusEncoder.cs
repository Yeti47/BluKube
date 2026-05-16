using Concentus;
using Concentus.Enums;

namespace BluKube.Server.Core.Engine.Audio;

/// <summary>
/// Thin wrapper around Concentus' Opus encoder configured for
/// <see cref="BluKube.Contracts.AudioFormat"/>.
/// </summary>
internal sealed class OpusEncoder : IDisposable
{
    private readonly IOpusEncoder _enc;

    // Per RFC 6716: Opus packets max 1275 bytes for single-frame, but encoder
    // can emit up to 4000 with internal multi-frame; 1500 is the safe MTU choice.
    public const int MaxPacketBytes = 1500;

    public OpusEncoder()
    {
        _enc = OpusCodecFactory.CreateEncoder(
            BluKube.Contracts.AudioFormat.SampleRate,
            BluKube.Contracts.AudioFormat.Channels,
            OpusApplication.OPUS_APPLICATION_AUDIO
        );
        _enc.Bitrate = BluKube.Contracts.AudioFormat.Bitrate;
    }

    public byte[] Encode(ReadOnlySpan<short> pcmFrame)
    {
        Span<byte> buf = stackalloc byte[MaxPacketBytes];
        int written = _enc.Encode(
            pcmFrame,
            BluKube.Contracts.AudioFormat.SamplesPerFrame,
            buf,
            buf.Length
        );
        return buf[..written].ToArray();
    }

    public void Dispose() => _enc.Dispose();
}
