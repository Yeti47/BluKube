using BluKube.Server.Core.Session;

namespace BluKube.Server.Hubs;

/// <summary>
/// Strongly-typed callbacks pushed from server to a connected client.
/// </summary>
public interface ISessionClient
{
    Task State(SessionState state);
}
