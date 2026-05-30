using System.Net;
using System.Net.Sockets;

namespace WebReaper.PlaygroundApi.Ssrf;

/// <summary>Thrown when a fetch is refused because the target resolves to a disallowed address.</summary>
public sealed class SsrfBlockedException(string host, string reason)
    : Exception($"Refused to connect to '{host}': {reason}.")
{
    public string Host { get; } = host;
}

/// <summary>
/// Builds an <see cref="HttpClient"/> whose connections are pinned to a
/// <see cref="SsrfPolicy"/>-validated IP. The <see cref="SocketsHttpHandler.ConnectCallback"/>
/// resolves the host itself, rejects the whole host if <em>any</em> resolved
/// address is blocked, and connects to a validated address, so a DNS-rebind
/// between resolution and connection cannot swap in an internal IP (the socket
/// connects to the exact address we checked). Redirects open new connections,
/// so each hop is re-validated by the same callback.
/// </summary>
public static class GuardedHttpClient
{
    public static HttpClient Create(TimeSpan timeout, long maxResponseBytes)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            ConnectCallback = GuardedConnectAsync,
        };

        var client = new HttpClient(handler)
        {
            Timeout = timeout,
            // Hard cap on buffered response bytes; ReadAsStringAsync throws past this.
            MaxResponseContentBufferSize = maxResponseBytes,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WebReaperPlayground/1.0 (+https://webreaper.ai)");
        return client;
    }

    private static async ValueTask<Stream> GuardedConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

        if (addresses.Length == 0)
            throw new SsrfBlockedException(host, "no DNS records");

        // Strict: if any resolved address is blocked, refuse the whole host
        // rather than cherry-picking a public one (mixed results are suspicious).
        foreach (var address in addresses)
            if (SsrfPolicy.IsBlockedAddress(address))
                throw new SsrfBlockedException(host, $"resolves to disallowed address {address}");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses[0], port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
