namespace BluKube.Server.Configuration;

/// <summary>
/// CORS settings. Locked off by default: the server is intended to be
/// reached from the TUI over loopback or LAN, not from a browser.
/// Set <see cref="Origins"/> to opt in for specific origins.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "BluKubeCors";

    /// <summary>
    /// Allowed origins. When empty, no CORS policy is registered.
    /// </summary>
    public string[] Origins { get; set; } = [];
}
