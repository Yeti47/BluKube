using Concentus;
using Silk.NET.OpenAL;

namespace BluKube.Client.Core.Audio;

/// <summary>
/// Streams Opus packets from a SignalR <c>StreamAudio</c> stream into the
/// default OpenAL playback device. Uses a small ring of source buffers
/// (~120 ms) to keep latency low without underruns.
/// </summary>
public sealed class AudioPlayer : IAsyncDisposable
{
    private const int BufferCount = 6; // 6 * 20 ms = 120 ms of pre-roll

    private readonly IOpusDecoder _decoder = OpusCodecFactory.CreateDecoder(
        AudioFormat.SampleRate, AudioFormat.Channels);

    private readonly short[] _pcm = new short[
        AudioFormat.SamplesPerFrame * AudioFormat.Channels];

    private ALContext? _alc;
    private AL? _al;
    private unsafe Device* _device;
    private unsafe Context* _context;
    private uint _source;
    private bool _disposed;

    public unsafe void Open()
    {
        if (_al is not null) return;

        _alc = ALContext.GetApi(soft: false);
        _al = AL.GetApi(soft: false);

        _device = _alc.OpenDevice(string.Empty);
        if (_device is null) throw new InvalidOperationException("OpenAL: cannot open default device");

        _context = _alc.CreateContext(_device, null);
        _alc.MakeContextCurrent(_context);

        _source = _al.GenSource();
        _al.SetSourceProperty(_source, SourceFloat.Gain, 1f);
    }

    /// <summary>
    /// Drains <paramref name="opusPackets"/> until cancellation. Returns when
    /// the upstream completes or <paramref name="ct"/> fires.
    /// </summary>
    // Sync helpers: locals in non-async methods live on the real stack,
    // so &local is safe to pass to native code.
    private unsafe uint UnqueueOneBuffer(AL al)
    {
        uint id = 0;
        al.SourceUnqueueBuffers(_source, 1, &id);
        return id;
    }

    private unsafe void QueueOneBuffer(AL al, uint bufferId)
    {
        al.SourceQueueBuffers(_source, 1, &bufferId);
    }

    private unsafe void UnqueueBuffers(AL al, Span<uint> buffers)
    {
        fixed (uint* ids = buffers)
        {
            al.SourceUnqueueBuffers(_source, buffers.Length, ids);
        }
    }

    private unsafe void UploadPcmToBuffer(AL al, uint bufferId, int decodedSamples)
    {
        fixed (short* p = _pcm)
        {
            al.BufferData(bufferId, BufferFormat.Stereo16, p,
                decodedSamples * AudioFormat.Channels * sizeof(short),
                AudioFormat.SampleRate);
        }
    }

    public async Task PlayAsync(IAsyncEnumerable<byte[]> opusPackets, CancellationToken ct)
    {
        Open();
        var al = _al!;

        var buffers = new uint[BufferCount];
        for (int i = 0; i < BufferCount; i++) buffers[i] = al.GenBuffer();

        var queue = new Queue<uint>(buffers);
        bool started = false;

        try
        {
            await foreach (var packet in opusPackets.WithCancellation(ct))
            {
                int decoded;
                try
                {
                    decoded = _decoder.Decode(packet, _pcm, AudioFormat.SamplesPerFrame);
                }
                catch
                {
                    continue; // skip malformed packet
                }
                if (decoded <= 0) continue;

                // Recycle any processed buffers back into the queue.
                al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
                while (processed-- > 0)
                {
                    uint released = UnqueueOneBuffer(al);
                    if (released != 0) queue.Enqueue(released);
                }

                if (queue.Count == 0)
                {
                    // All buffers in flight; wait briefly for the source to drain one.
                    await Task.Delay(2, ct);
                    al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out processed);
                    while (processed-- > 0)
                    {
                        uint released = UnqueueOneBuffer(al);
                        if (released != 0) queue.Enqueue(released);
                    }
                    if (queue.Count == 0) continue; // drop frame to avoid stall
                }

                var buf = queue.Dequeue();
                UploadPcmToBuffer(al, buf, decoded);
                QueueOneBuffer(al, buf);

                if (!started)
                {
                    al.SourcePlay(_source);
                    started = true;
                }
                else
                {
                    // Restart if the source ran dry between packets.
                    al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
                    if (state != (int)SourceState.Playing) al.SourcePlay(_source);
                }
            }
        }
        finally
        {
            try { al.SourceStop(_source); } catch { }
            try
            {
                al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out int queued);
                while (queued > 0)
                {
                    var batchSize = Math.Min(queued, buffers.Length);
                    UnqueueBuffers(al, buffers.AsSpan(0, batchSize));
                    queued -= batchSize;
                }
            }
            catch { }
            foreach (var b in buffers)
            {
                try { al.DeleteBuffer(b); } catch { }
            }
        }
    }

    public unsafe ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        try
        {
            if (_al is not null && _source != 0)
            {
                _al.SourceStop(_source);
                _al.DeleteSource(_source);
            }
        }
        catch { }

        try
        {
            if (_alc is not null)
            {
                if (_context is not null)
                {
                    _alc.MakeContextCurrent(null);
                    _alc.DestroyContext(_context);
                }
                if (_device is not null)
                {
                    _alc.CloseDevice(_device);
                }
            }
        }
        catch { }

        _al?.Dispose();
        _alc?.Dispose();
        try { _decoder.Dispose(); } catch { }
        return ValueTask.CompletedTask;
    }
}
