namespace BluKube.Client.Core;

/// <summary>
/// Persistent storage for client configuration (server URL, auth token).
/// TUI implementation writes TOML/JSON in the user's config directory;
/// browser implementations may use localStorage or per-user server state.
/// </summary>
public interface IConfigStore
{
    Task<ConnectionSettings?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(ConnectionSettings settings, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}
