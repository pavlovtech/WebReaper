using System.Collections.Concurrent;
using WebReaper.Builders;
using WebReaper.Domain.Parsing;
using WebReaper.Infra.Abstract;
using WebReaper.IntegrationTests.Fixtures;
using WebReaper.Sinks.Models;
using WebReaper.TestServer;
using Xunit;
using Xunit.Abstractions;

namespace WebReaper.IntegrationTests;

/// <summary>
/// Retry + failure coverage against <c>/fail</c> (fail the first N hits, then
/// 200) and <c>/slow</c>. The server records a per-key hit count, so the number
/// of times the retry policy re-requested is directly observable — no need to
/// instrument the library. ADR-0026: the default policy is 4 attempts (one +
/// three retries); cancellation propagates without a retry; on exhaustion the
/// last exception propagates out of <c>RunAsync</c> (ScraperEngine has no
/// per-job swallow).
/// </summary>
[Collection("LocalSite")]
[Trait("Category", "LocalServer")]
public sealed class RetryAndFailureTests
{
    private readonly LocalTestSite _site;
    private readonly ITestOutputHelper _output;

    public RetryAndFailureTests(LocalSiteFixture fixture, ITestOutputHelper output)
    {
        _site = fixture.Site;
        _output = output;
    }

    /// <summary>A test-controlled <see cref="IRetryPolicy"/> (the core
    /// <c>FixedAttemptsRetryPolicy</c> is internal) — N attempts, no delay,
    /// cancellation never retried. Mirrors the ADR-0026 contract.</summary>
    private sealed class FixedAttempts(int maxAttempts) : IRetryPolicy
    {
        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { return await action(cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch when (attempt < maxAttempts) { /* retry */ }
            }
        }
    }

    [Fact]
    public async Task Default_policy_attempts_four_times_then_propagates()
    {
        const string key = "default-exhaust";

        var ex = await Record.ExceptionAsync(async () =>
        {
            await using var engine = await ScraperEngineBuilder
                .Crawl(_site.Url($"/fail?key={key}&status=500&times=99"))
                .AsMarkdown()
                .WithLogger(new TestOutputLogger(_output))
                .StopWhenAllLinksProcessed()
                .BuildAsync();
            await engine.RunAsync();
        });

        Assert.NotNull(ex);                         // permanent failure bubbles out
        Assert.Equal(4, _site.FailHits(key));       // ADR-0026 default: 4 attempts
    }

    [Fact]
    public async Task Custom_retry_policy_overrides_the_default_attempt_count()
    {
        const string key = "custom-two";

        var ex = await Record.ExceptionAsync(async () =>
        {
            await using var engine = await ScraperEngineBuilder
                .Crawl(_site.Url($"/fail?key={key}&status=500&times=99"))
                .AsMarkdown()
                .WithRetryPolicy(new FixedAttempts(2))
                .WithLogger(new TestOutputLogger(_output))
                .StopWhenAllLinksProcessed()
                .BuildAsync();
            await engine.RunAsync();
        });

        Assert.NotNull(ex);
        Assert.Equal(2, _site.FailHits(key));       // honored the custom policy
    }

    [Fact]
    public async Task Transient_failure_is_retried_then_succeeds()
    {
        const string key = "recover";
        var records = new ConcurrentQueue<ParsedData>();

        await using (var engine = await ScraperEngineBuilder
            .Crawl(_site.Url($"/fail?key={key}&status=503&times=2"))
            .Extract(new Schema { new("title", ".title") })
            .WithLogger(new TestOutputLogger(_output))
            .Subscribe(records.Enqueue)
            .StopWhenAllLinksProcessed()
            .BuildAsync())
        {
            await engine.RunAsync();               // no throw: 3rd attempt succeeds
        }

        var rec = Assert.Single(records);
        Assert.Equal("Recovered", rec.Data["title"]!.GetValue<string>().Trim());
        Assert.Equal(3, _site.FailHits(key));      // 2 failures + 1 success
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_is_not_retried()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var ex = await Record.ExceptionAsync(async () =>
        {
            await using var engine = await ScraperEngineBuilder
                .Crawl(_site.Url("/slow?ms=5000"))
                .AsMarkdown()
                .WithLogger(new TestOutputLogger(_output))
                .StopWhenAllLinksProcessed()
                .BuildAsync();
            await engine.RunAsync(cts.Token);
        });

        // The in-flight load is aborted at ~300ms; OCE flows out unretried.
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }
}
