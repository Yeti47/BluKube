using Microsoft.Extensions.Logging;

namespace BluKube.Server.Core.Engine.Audio;

internal sealed class PulseAudioOutputDeviceFactory(ILogger<PulseAudioOutputDeviceFactory> logger)
    : IAudioOutputDeviceFactory
{
    public async Task<IAudioOutputDevice> CreateAsync(CancellationToken ct) =>
        await PulseAudioOutputDevice.CreateAsync(logger, ct);
}
