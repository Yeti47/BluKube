namespace BluKube.Server.Core.Playback;

public interface ISessionManager
{
    Task<IPlayerSession> CreateAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<IPlayerSession>> ListAsync();
    Task<IPlayerSession?> GetAsync(Guid sessionId);
    Task<bool> RemoveAsync(Guid sessionId);
}
