using System.Net;
using Microsoft.Extensions.Logging;
using WebReaper.Core.CookieStorage.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Proxy;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Builders;

/// <summary>
/// The ADR-0009 distributed-worker reduced shell: builds a bare
/// <see cref="ISpider"/> from the <see cref="Domain.ScraperConfig"/> the
/// worker hands to <see cref="BuildSpider"/> (ADR-0034 — the shell no longer
/// reads config storage itself). Use it in the DIY-distributed pattern — pull
/// one <c>Job</c> off your own queue, crawl it with
/// <c>spider.CrawlAsync(job)</c>, and re-enqueue the returned child jobs
/// yourself (see <c>Examples/WebReaper.AzureFuncs</c>).
/// <para>
/// It deliberately has no <c>BuildAsync</c> and no crawl-shape (no start
/// URLs / schema / <c>Follow</c>): a worker's crawl definition is authored
/// and persisted by the start endpoint
/// (<see cref="ScraperEngineBuilder.Build"/>), not here. This is the "two
/// seams, not one bug" split (ADR-0025) — the structural guarantee that
/// <see cref="ScraperEngineBuilder.BuildAsync"/> is unreachable without a
/// Crawl seed lives in <see cref="ScraperEngineBuilder"/>'s internal
/// constructor; this seam is config-agnostic by design.
/// </para>
/// <para>
/// Distributed adapters (a shared Redis/Mongo link tracker, latch, and the
/// config store the worker reads its <see cref="Domain.ScraperConfig"/> from)
/// are wired by hand here — pass a public concrete or a DI-resolved instance,
/// as the ADR-0009 pattern intends. The satellite builder sugar
/// (<c>.WithRedis*()</c>, …) is the engine-path convenience and stays on
/// <see cref="ScraperEngineBuilder"/>.
/// </para>
/// </summary>
public class DistributedSpiderBuilder
{
    private readonly SpiderBuilder _spiderBuilder = new();

    /// <summary>Route library logging to your <see cref="ILogger"/>.</summary>
    public DistributedSpiderBuilder WithLogger(ILogger logger)
    {
        _spiderBuilder.WithLogger(logger);
        return this;
    }

    /// <summary>Register a custom <see cref="IContentExtractor"/> — an
    /// alternative extraction strategy, or the deterministic <c>SchemaFold</c>
    /// over a custom <c>ISchemaBackend</c> (ADR-0002 / ADR-0039) — instead of
    /// the default AngleSharp/CSS fold.</summary>
    public DistributedSpiderBuilder WithContentExtractor(IContentExtractor extractor)
    {
        _spiderBuilder.WithContentExtractor(extractor);
        return this;
    }

    /// <summary>Parse responses as JSON instead of HTML (issue #27).</summary>
    public DistributedSpiderBuilder WithJsonContentParser()
    {
        _spiderBuilder.WithJsonContentParser();
        return this;
    }

    /// <summary>Parse responses as HTML with XPath 1.0 selectors
    /// (discussion #17; ADR 0002/0007).</summary>
    public DistributedSpiderBuilder WithXPathContentParser()
    {
        _spiderBuilder.WithXPathContentParser();
        return this;
    }

    /// <summary>Register the Dynamic (headless-browser) transport
    /// (ADR-0004/0009). Core is HTTP-only by default.</summary>
    public DistributedSpiderBuilder WithLoadTransport(
        Func<ICookiesStorage, IProxyProvider?, ILogger, IPageLoadTransport> dynamicTransportFactory)
    {
        _spiderBuilder.WithLoadTransport(dynamicTransportFactory);
        return this;
    }

    /// <summary>Route page loads through a rotating proxy.</summary>
    public DistributedSpiderBuilder WithProxies(IProxyProvider proxyProvider)
    {
        _spiderBuilder.WithProxies(proxyProvider);
        return this;
    }

    /// <summary>Rotate over proxies from <paramref name="source"/>, keeping
    /// only those every validator approves.</summary>
    public DistributedSpiderBuilder WithValidatedProxies(
        IProxySource source,
        IEnumerable<IProxyValidator> validators,
        ValidatedProxyProviderOptions? options = null)
    {
        _spiderBuilder.WithValidatedProxies(source, validators, options);
        return this;
    }

    /// <summary><see cref="WithValidatedProxies(IProxySource, IEnumerable{IProxyValidator}, ValidatedProxyProviderOptions)"/>
    /// with the validators as params.</summary>
    public DistributedSpiderBuilder WithValidatedProxies(IProxySource source, params IProxyValidator[] validators)
    {
        _spiderBuilder.WithValidatedProxies(source, validators);
        return this;
    }

    /// <summary>Seed the crawl's cookie container (e.g. an authenticated
    /// session) before any page loads.</summary>
    public DistributedSpiderBuilder SetCookies(Action<CookieContainer> authorize)
    {
        _spiderBuilder.SetCookies(authorize);
        return this;
    }

    /// <summary>Use a custom <see cref="ICookiesStorage"/>.</summary>
    public DistributedSpiderBuilder WithCookieStorage(ICookiesStorage cookiesStorage)
    {
        _spiderBuilder.WithCookieStorage(cookiesStorage);
        return this;
    }

    /// <summary>Persist cookies to a file so an authenticated session
    /// survives restarts.</summary>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is
    /// null/empty/whitespace.</exception>
    public DistributedSpiderBuilder WithFileCookieStorage(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        _spiderBuilder.WithFileCookieStorage(fileName);
        return this;
    }

    /// <summary>
    /// Build the bare <see cref="ISpider"/> for this distributed worker from
    /// the <see cref="Domain.ScraperConfig"/> the start endpoint persisted
    /// (<see cref="ScraperEngineBuilder.Build"/>) — the worker fetches it from
    /// shared storage and passes it here (ADR-0034). No Crawl seed or schema
    /// is needed: a worker is config-agnostic by construction (ADR-0025).
    /// <paramref name="config"/> is required, so a worker spider cannot be
    /// built without one — the pre-0034 silent empty-config footgun is gone.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is
    /// null.</exception>
    public ISpider BuildSpider(ScraperConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _spiderBuilder.WithConfig(config);
        return _spiderBuilder.Build();
    }
}
