using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace BluKube.Web.Clients.ErrorHandling;

public sealed class ClientStartupFailedEventArgs(ClientStartupException error) : EventArgs
{
    public ClientStartupException Error { get; } = error;
}

public abstract class ClientStartupException(string message, Exception innerException)
    : Exception(message, innerException)
{
    public static bool TryCreate(
        Exception ex,
        [NotNullWhen(true)] out ClientStartupException? startupException
    )
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
            {
                startupException = new Authorization(
                    "Authentication failed. Please check your token and try again.",
                    ex
                );
                return true;
            }

            if (current is HttpRequestException { StatusCode: HttpStatusCode.Forbidden })
            {
                startupException = new Authorization(
                    "Access denied. Please check that your token has permission to use this server.",
                    ex
                );
                return true;
            }

            if (
                current.Message.Contains("401", StringComparison.Ordinal)
                && current.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
            )
            {
                startupException = new Authorization(
                    "Authentication failed. Please check your token and try again.",
                    ex
                );
                return true;
            }

            if (
                current.Message.Contains("403", StringComparison.Ordinal)
                && current.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
            )
            {
                startupException = new Authorization(
                    "Access denied. Please check that your token has permission to use this server.",
                    ex
                );
                return true;
            }

            if (current is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
            {
                startupException = new Connection(
                    "No BluKube server was found at this URL. Please check the server address and try again.",
                    ex
                );
                return true;
            }

            if (current is HttpRequestException { StatusCode: null } || current is SocketException)
            {
                startupException = new Connection(
                    "Could not reach the configured server. Please check the server address and try again.",
                    ex
                );
                return true;
            }

            if (current is TimeoutException or TaskCanceledException)
            {
                startupException = new Connection(
                    "Timed out while connecting to the server. Please check the server address and try again.",
                    ex
                );
                return true;
            }

            var text = current.Message;
            if (
                text.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
                || text.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
                || text.Contains("no such host", StringComparison.OrdinalIgnoreCase)
                || text.Contains("name or service not known", StringComparison.OrdinalIgnoreCase)
                || text.Contains(
                    "nodename nor servname provided",
                    StringComparison.OrdinalIgnoreCase
                )
                || text.Contains("network is unreachable", StringComparison.OrdinalIgnoreCase)
                || text.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            )
            {
                startupException = new Connection(
                    "Could not reach the configured server. Please check the server address and try again.",
                    ex
                );
                return true;
            }
        }

        startupException = null;
        return false;
    }

    public sealed class Authorization(string message, Exception innerException)
        : ClientStartupException(message, innerException);

    public sealed class Connection(string message, Exception innerException)
        : ClientStartupException(message, innerException);
}
