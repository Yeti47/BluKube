using BluKube.Server.Core.Session;

namespace BluKube.Server.Core.Session;

public interface IBrowserSession : IAsyncDisposable
{
    Guid Id { get; }
    
    Task<SessionSnapshot> DispatchAsync(ClientEvent clientEvent, CancellationToken cancellationToken = default);
    
    IAsyncEnumerable<SessionSnapshot> Snapshots(CancellationToken cancellationToken = default);
}