namespace BluKube.Server.Configuration;

/// <summary>
/// Bearer-token auth settings. The server accepts a single shared
/// secret. If no token is provided in configuration, one is generated
/// at startup and persisted to <see cref="TokenFile"/>.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Pre-shared bearer token. When set, takes precedence over any
    /// token file. Typically supplied via the BLUKUBE_TOKEN env var.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Filesystem path used to persist a generated token across
    /// restarts. Read at startup; written only when no token is
    /// configured and the file does not yet exist.
    /// </summary>
    public string TokenFile { get; set; } = "/var/lib/blukube/token";
}
