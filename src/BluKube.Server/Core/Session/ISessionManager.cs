namespace BluKube.Server.Core.Session;

public interface ISessionManager
{
    Task<IBrowserSession> CreateSessionAsync(CancellationToken cancellationToken = default);
    Task<IBrowserSession?> GetSessionAsync(Guid sessionId);
    Task<bool> RemoveSessionAsync(Guid sessionId);
    Task<IReadOnlyList<IBrowserSession>> ListSessionsAsync();
}