using WebReaper.Builders;
using WebReaper.Core.Spider.Abstract;

namespace WebReaper.UnitTests;

public class BuildSpiderTests
{
    // ADR-0009 capstone: the distributed-worker pattern (crawl one queued
    // Job, re-enqueue its children — Examples/WebReaper.AzureFuncs) needs a
    // bare ISpider. SpiderBuilder is now internal; the public seam is
    // ScraperEngineBuilder.BuildSpider(). Unlike BuildAsync() it does NOT
    // require Get/Parse (the worker's ScraperConfig is persisted separately
    // and read from storage at crawl time), so configuring then BuildSpider()
    // succeeds with neither a start URL nor a schema.
    [Fact]
    public void BuildSpider_returns_a_configured_ISpider_without_requiring_Get_or_Parse()
    {
        var spider = new ScraperEngineBuilder()
            .WriteToConsole()
            .BuildSpider();

        Assert.NotNull(spider);
        Assert.IsAssignableFrom<ISpider>(spider);
    }
}
