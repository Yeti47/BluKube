namespace BluKube.Client.Core;

/// <summary>
/// Connection settings for a BluKube client. Resolved from
/// <see cref="IConfigStore"/> at startup and may be updated at runtime
/// (e.g. after a token re-prompt).
/// </summary>
public sealed record ConnectionSettings(Uri ServerUrl, string? Token)
{
    public Uri HubUrl => new(ServerUrl, "/hubs/session");
    public Uri RestBase => new(ServerUrl, "/v1/");
}
