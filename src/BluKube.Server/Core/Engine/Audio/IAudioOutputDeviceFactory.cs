namespace BluKube.Server.Core.Engine.Audio;

public interface IAudioOutputDeviceFactory
{
    Task<IAudioOutputDevice> CreateAsync(CancellationToken ct);
}
