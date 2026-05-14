using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace BluKube.Server.Configuration;

/// <summary>
/// Resolves the effective bearer token at startup: prefers the value
/// configured in <see cref="AuthOptions.Token"/>, otherwise reads (and
/// if missing, generates and persists) the token at
/// <see cref="AuthOptions.TokenFile"/>.
/// </summary>
public sealed class AuthTokenProvider(IOptions<AuthOptions> options, ILogger<AuthTokenProvider> logger)
{
    private readonly Lazy<string> _token = new(() => ResolveToken(options.Value, logger));

    public string Token => _token.Value;

    private static string ResolveToken(AuthOptions opts, ILogger<AuthTokenProvider> logger)
    {
        if (!string.IsNullOrWhiteSpace(opts.Token))
        {
            var configured = opts.Token.Trim();
            logger.LogInformation("Auth token loaded from configuration.");
            return configured;
        }

        var file = opts.TokenFile;
        if (File.Exists(file))
        {
            var existing = File.ReadAllText(file).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                logger.LogInformation("Auth token loaded from {File}.", file);
                return existing;
            }
        }

        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        try
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(file, generated);
            logger.LogWarning(
                "No auth token configured. Generated and persisted a new one to {File}. " +
                "Use BLUKUBE_TOKEN to pin an explicit value.", file);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "No auth token configured and {File} is not writable. " +
                "Using an in-memory token (will rotate on restart).", file);
        }

        return generated;
    }
}
