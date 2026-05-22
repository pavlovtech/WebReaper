using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Builders;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

public class BuildSpiderTests
{
    // ADR-0025: the distributed-worker reduced shell (ADR-0009) is its own
    // seam, DistributedSpiderBuilder — "two seams, not one bug". Unlike the
    // engine path it has no Crawl seed and no BuildAsync. ADR-0034: the
    // worker's ScraperConfig is authored/persisted by the start endpoint, and
    // the worker fetches it and passes it to BuildSpider — so building a bare
    // ISpider needs a ScraperConfig but still no start URL or schema. (The
    // structural guarantee that BuildAsync is unreachable without a seed lives
    // on ScraperEngineBuilder's internal ctor.)
    [Fact]
    public void DistributedSpiderBuilder_builds_an_ISpider_from_a_config_without_a_Crawl_seed()
    {
        var config = new ScraperConfig(
            ParsingScheme: null,
            LinkPathSelectors: ImmutableQueue<LinkPathSelector>.Empty,
            StartUrls: Array.Empty<string>(),
            UrlBlackList: Array.Empty<string>());

        var spider = new DistributedSpiderBuilder()
            .WithLogger(NullLogger.Instance)
            .BuildSpider(config);

        Assert.NotNull(spider);
        Assert.IsAssignableFrom<ISpider>(spider);
    }
}
