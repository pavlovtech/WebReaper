using System.Net;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Proxy.Concrete;

/// <summary>
/// A fixed set of proxies known up front (e.g. from config or a paid
/// plan). The list is captured once at construction.
/// </summary>
public sealed class StaticProxySource : IProxySource
{
    private readonly IReadOnlyList<WebProxy> _proxies;

    public StaticProxySource(IEnumerable<WebProxy> proxies)
    {
        ArgumentNullException.ThrowIfNull(proxies);
        _proxies = proxies.ToArray();
    }

    /// <summary>
    /// Build a source from <c>scheme://host:port</c> strings, e.g.
    /// <c>http://1.2.3.4:8080</c>. Scheme defaults to http if omitted.
    /// </summary>
    public static StaticProxySource FromAddresses(IEnumerable<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        var proxies = addresses.Select(a =>
        {
            var uri = a.Contains("://", StringComparison.Ordinal) ? a : "http://" + a;
            return new WebProxy(new Uri(uri));
        });

        return new StaticProxySource(proxies);
    }

    public Task<IReadOnlyList<WebProxy>> GetCandidatesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_proxies);
}
