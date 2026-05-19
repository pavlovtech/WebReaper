using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Builders;
using WebReaper.Core.Spider.Abstract;

namespace WebReaper.UnitTests;

public class BuildSpiderTests
{
    // ADR-0025: the distributed-worker reduced shell (ADR-0009) is its own
    // seam, DistributedSpiderBuilder — "two seams, not one bug". Unlike the
    // engine path it has no Crawl seed and no BuildAsync: the worker's
    // ScraperConfig is authored/persisted by the start endpoint and read from
    // shared storage at crawl time, so building a bare ISpider needs neither a
    // start URL nor a schema. (The structural guarantee that BuildAsync is
    // unreachable without a seed lives on ScraperEngineBuilder's internal ctor.)
    [Fact]
    public void DistributedSpiderBuilder_builds_an_ISpider_without_a_Crawl_seed()
    {
        var spider = new DistributedSpiderBuilder()
            .WithLogger(NullLogger.Instance)
            .BuildSpider();

        Assert.NotNull(spider);
        Assert.IsAssignableFrom<ISpider>(spider);
    }
}
