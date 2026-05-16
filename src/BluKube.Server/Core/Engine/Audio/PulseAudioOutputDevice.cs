using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace BluKube.Server.Core.Engine.Audio;

/// <summary>
/// Per-session PulseAudio null sink + monitor. Brave is launched with
/// <c>PULSE_SINK</c> pointed here, so all of its audio lands in the sink.
/// Subscribers spawn one <c>parec</c> process each, reading raw s16le PCM
/// from <c>{sink}.monitor</c>, and receive Opus-encoded frames.
/// </summary>
internal sealed class PulseAudioOutputDevice : IAudioOutputDevice
{
    private readonly ILogger _logger;
    private readonly string _moduleId;
    private bool _disposed;

    public string SinkName { get; }

    private PulseAudioOutputDevice(string sinkName, string moduleId, ILogger logger)
    {
        SinkName = sinkName;
        _moduleId = moduleId;
        _logger = logger;
    }

    public static async Task<PulseAudioOutputDevice> CreateAsync(
        ILogger logger,
        CancellationToken ct
    )
    {
        var sinkName = $"blukube_{Guid.NewGuid():N}";
        var args =
            $"load-module module-null-sink sink_name={sinkName} sink_properties=device.description=BluKube_{sinkName}";
        var (stdout, stderr, exit) = await RunPactlAsync(args, ct);
        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"pactl load-module failed (exit {exit}): {stderr.Trim()}"
            );
        }

        var moduleId = stdout.Trim();
        if (string.IsNullOrEmpty(moduleId) || !moduleId.All(char.IsDigit))
        {
            throw new InvalidOperationException(
                $"pactl returned unexpected module id '{moduleId}'"
            );
        }

        logger.LogInformation(
            "Created PulseAudio sink {Sink} (module {Module})",
            sinkName,
            moduleId
        );
        return new PulseAudioOutputDevice(sinkName, moduleId, logger);
    }

    public async IAsyncEnumerable<byte[]> StreamOpusAsync(
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PulseAudioOutputDevice));

        using var encoder = new OpusEncoder();
        var psi = new ProcessStartInfo("parec")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add($"--device={SinkName}.monitor");
        psi.ArgumentList.Add("--rate=" + BluKube.Contracts.AudioFormat.SampleRate);
        psi.ArgumentList.Add("--channels=" + BluKube.Contracts.AudioFormat.Channels);
        psi.ArgumentList.Add("--format=s16le");
        psi.ArgumentList.Add("--raw");
        psi.ArgumentList.Add("--latency-msec=" + BluKube.Contracts.AudioFormat.FrameMilliseconds);

        using var proc = new Process { StartInfo = psi };
        if (!proc.Start())
        {
            throw new InvalidOperationException("Failed to start parec");
        }

        // Drain stderr so the subprocess never blocks writing diagnostics.
        _ = Task.Run(
            async () =>
            {
                try
                {
                    var err = await proc.StandardError.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(err))
                    {
                        _logger.LogDebug("parec stderr ({Sink}): {Err}", SinkName, err.Trim());
                    }
                }
                catch
                { /* parec gone */
                }
            },
            CancellationToken.None
        );

        await using var killOnCancel = ct.Register(() =>
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch { }
        });

        var pcmBytes = new byte[BluKube.Contracts.AudioFormat.PcmBytesPerFrame];
        var pcmShorts = new short[
            BluKube.Contracts.AudioFormat.SamplesPerFrame * BluKube.Contracts.AudioFormat.Channels
        ];
        var stdout = proc.StandardOutput.BaseStream;

        while (!ct.IsCancellationRequested)
        {
            int read = 0;
            while (read < pcmBytes.Length)
            {
                int n;
                try
                {
                    n = await stdout.ReadAsync(pcmBytes.AsMemory(read), ct);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                if (n == 0)
                {
                    // parec exited or pipe closed.
                    yield break;
                }
                read += n;
            }

            Buffer.BlockCopy(pcmBytes, 0, pcmShorts, 0, pcmBytes.Length);
            byte[] packet;
            try
            {
                packet = encoder.Encode(pcmShorts);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Opus encode failed; skipping frame");
                continue;
            }
            yield return packet;
        }

        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            var (_, stderr, exit) = await RunPactlAsync(
                $"unload-module {_moduleId}",
                CancellationToken.None
            );
            if (exit != 0)
            {
                _logger.LogWarning(
                    "pactl unload-module {Module} failed (exit {Exit}): {Err}",
                    _moduleId,
                    exit,
                    stderr.Trim()
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unload PulseAudio module {Module}", _moduleId);
        }
    }

    private static async Task<(string Stdout, string Stderr, int Exit)> RunPactlAsync(
        string args,
        CancellationToken ct
    )
    {
        var psi = new ProcessStartInfo("pactl")
        {
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc =
            Process.Start(psi) ?? throw new InvalidOperationException("Failed to start pactl");

        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync(ct);
        return (await outTask, await errTask, proc.ExitCode);
    }
}
