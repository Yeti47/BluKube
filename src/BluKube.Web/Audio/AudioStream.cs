using BluKube.Contracts;
using BluKube.Web.Clients;
using Concentus;
using Microsoft.JSInterop;

namespace BluKube.Web.Audio;

public sealed class AudioStream(IJSRuntime js, ClientSession session)
{
    public Task? PumpTask { get; private set; }

    public void Start(Action<string>? onError = null)
    {
        PumpTask = PumpAudioAsync(onError);
    }

    public async Task ResumeAsync()
    {
        try
        {
            await js.InvokeVoidAsync("xtermBridge.resumeAudio");
        }
        catch { }
    }

    public void Clear() => PumpTask = null;

    private async Task PumpAudioAsync(Action<string>? onError)
    {
        var connection = session.Connection;
        if (connection is null)
            return;

        var decoder = OpusCodecFactory.CreateDecoder(AudioFormat.SampleRate, AudioFormat.Channels);
        var pcm = new short[AudioFormat.SamplesPerFrame * AudioFormat.Channels];
        var batch = new byte[AudioFormat.PcmBytesPerFrame * 4];
        var batchBytes = 0;
        var batchFrames = 0;

        try
        {
            await foreach (var packet in connection.StreamAudioAsync(session.Token))
            {
                int decodedSamples;
                try
                {
                    decodedSamples = decoder.Decode(packet, pcm, AudioFormat.SamplesPerFrame);
                }
                catch
                {
                    continue;
                }

                if (decodedSamples <= 0)
                    continue;

                var byteCount = decodedSamples * AudioFormat.Channels * sizeof(short);
                if (batchBytes + byteCount > batch.Length && batchBytes > 0)
                {
                    await js.InvokeVoidAsync(
                        "xtermBridge.writeAudio",
                        session.Token,
                        Convert.ToBase64String(batch, 0, batchBytes)
                    );
                    batchBytes = 0;
                    batchFrames = 0;
                }

                Buffer.BlockCopy(pcm, 0, batch, batchBytes, byteCount);
                batchBytes += byteCount;
                batchFrames++;

                if (batchFrames < 4)
                    continue;

                await js.InvokeVoidAsync(
                    "xtermBridge.writeAudio",
                    session.Token,
                    Convert.ToBase64String(batch, 0, batchBytes)
                );
                batchBytes = 0;
                batchFrames = 0;
            }

            if (batchBytes > 0)
            {
                await js.InvokeVoidAsync(
                    "xtermBridge.writeAudio",
                    session.Token,
                    Convert.ToBase64String(batch, 0, batchBytes)
                );
            }
        }
        catch (OperationCanceledException) { }
        catch (JSDisconnectedException) { }
        catch (Exception ex)
        {
            onError?.Invoke($"audio stopped: {ex.Message}");
        }
    }
}
