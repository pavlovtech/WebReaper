using System.Net;
using System.Net.Security;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Proxy.Concrete;

/// <summary>
/// Validates a proxy by issuing a real HTTP request through it and
/// checking the response succeeds.
///
/// This deliberately does NOT use ICMP ping: ping proves the host is
/// up, not that it forwards HTTP traffic, and many proxies/firewalls
/// drop ICMP entirely. An actual request through the proxy is the only
/// check that means "this proxy works for scraping".
/// </summary>
public sealed class HttpProxyValidator : IProxyValidator
{
    private readonly Uri _testUrl;

    /// <summary>Validate proxies by fetching <paramref name="testUrl"/>
    /// through each one.</summary>
    /// <param name="testUrl">
    /// URL fetched through the proxy. Default is Google's connectivity
    /// endpoint, which returns an empty 204 quickly.
    /// </param>
    public HttpProxyValidator(string testUrl = "http://www.gstatic.com/generate_204")
        => _testUrl = new Uri(testUrl);

    /// <inheritdoc/>
    public async Task<bool> IsValidAsync(WebProxy proxy, CancellationToken cancellationToken = default)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                Proxy = proxy,
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = delegate { return true; }
                }
            };

            using var client = new HttpClient(handler);

            using var response = await client.GetAsync(
                _testUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancelled (shutdown / timeout) — propagate so the
            // provider can stop, not be recorded as a "bad proxy".
            throw;
        }
        catch
        {
            // Any transport failure (timeout, refused, proxy auth, DNS)
            // means the proxy is unusable. Never throw for that.
            return false;
        }
    }
}
