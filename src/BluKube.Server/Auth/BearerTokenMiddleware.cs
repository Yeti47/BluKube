using System.Security.Cryptography;
using System.Text;
using BluKube.Server.Configuration;

namespace BluKube.Server.Auth;

/// <summary>
/// Tiny bearer-token gate. Allows unauthenticated access to a small
/// set of public paths (health probes, OpenAPI). Everything else
/// requires a matching <c>Authorization: Bearer &lt;token&gt;</c> header,
/// or — for the SignalR hub negotiation — an <c>access_token</c> query
/// parameter.
/// </summary>
public sealed class BearerTokenMiddleware(RequestDelegate next, AuthTokenProvider tokens)
{
    private readonly byte[] _expected = Encoding.UTF8.GetBytes(tokens.Token);

    private static readonly string[] PublicPathPrefixes =
    [
        "/health",
        "/alive",
        "/openapi",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (path == "/" || IsPublic(path))
        {
            await next(context);
            return;
        }

        if (TryGetToken(context, out var presented) && FixedTimeEquals(presented, _expected))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
    }

    private static bool IsPublic(string path)
    {
        foreach (var prefix in PublicPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryGetToken(HttpContext context, out byte[] token)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(header) &&
            header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = Encoding.UTF8.GetBytes(header.AsSpan(7).Trim().ToString());
            return token.Length > 0;
        }

        if (context.Request.Query.TryGetValue("access_token", out var qs) && qs.Count > 0)
        {
            var value = qs[0];
            if (!string.IsNullOrEmpty(value))
            {
                token = Encoding.UTF8.GetBytes(value);
                return true;
            }
        }

        token = [];
        return false;
    }

    private static bool FixedTimeEquals(byte[] a, byte[] b)
        => CryptographicOperations.FixedTimeEquals(a, b);
}
