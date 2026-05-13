namespace BluKube.Server.Configuration;

/// <summary>
/// Server-wide session limits. Each session owns one isolated browser
/// engine, so these are real resource caps — not just policy knobs.
/// </summary>
public sealed class SessionLimits
{
    public const string SectionName = "Session";

    /// <summary>
    /// Hard cap on the number of concurrent sessions the server will host.
    /// Attempts to create a session beyond this limit are rejected.
    /// </summary>
    public int MaxSessions { get; set; } = 4;

    /// <summary>
    /// A session is closed and disposed once it has had no activity
    /// (no command, no state subscription) for this duration.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How often the idle sweeper checks for expired sessions.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);
}
