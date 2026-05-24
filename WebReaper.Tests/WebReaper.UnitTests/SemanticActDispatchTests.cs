using System.Collections.Immutable;
using WebReaper.Builders;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Core.Actions.Concrete;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;
using WebReaper.Serialization;

namespace WebReaper.UnitTests;

// ADR-0050: the SemanticAct arm + the resolve-once-per-crawl coordinator
// pin every guarantee the ADR makes:
//   * Cache HIT  -> reuse the cached arm, no resolver call.
//   * Cache MISS -> call the resolver, dispatch, cache on success.
//   * Cached-arm dispatch FAILURE -> invalidate + re-resolve (page changed).
//   * Resolver returns null  -> SemanticActResolutionException.
//   * Resolver THROWS        -> SemanticActResolutionException (wrapping).
//   * Resolver returns SemanticAct (would loop) -> SemanticActResolutionException.
//   * Freshly-resolved arm dispatch THROWS -> surface, do NOT cache.
//   * OperationCanceledException always propagates, never wrapped.
//
// The coordinator lives in core (testable without IPage / Chromium); the
// Puppeteer transport delegates each PageAction.SemanticAct case to it.
public class SemanticActDispatchTests
{
    [Fact]
    public async Task First_invocation_calls_resolver_then_dispatches_then_caches()
    {
        var resolver = new RecordingResolver(_ => new PageAction.Click(".btn"));
        var coord = new SemanticActCoordinator(resolver, new CapturingLogger());
        var dispatched = new List<PageAction>();

        await coord.DispatchAsync(
            "click sign in",
            getHtmlAsync: _ => Task.FromResult("<html/>"),
            dispatch: (a, _) => { dispatched.Add(a); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal(1, resolver.CallCount);
        Assert.Equal(1, coord.CacheCount);
        Assert.IsType<PageAction.Click>(Assert.Single(dispatched));
        Assert.IsType<PageAction.Click>(coord.TryGetCached("click sign in"));
    }

    [Fact]
    public async Task Subsequent_invocations_with_same_intent_are_cache_hits_no_resolver_call()
    {
        var resolver = new RecordingResolver(_ => new PageAction.Click(".btn"));
        var coord = new SemanticActCoordinator(resolver, new CapturingLogger());
        var dispatched = new List<PageAction>();
        var htmlReads = 0;

        for (var i = 0; i < 3; i++)
        {
            await coord.DispatchAsync(
                "click sign in",
                getHtmlAsync: _ => { htmlReads++; return Task.FromResult("<html/>"); },
                dispatch: (a, _) => { dispatched.Add(a); return Task.CompletedTask; },
                CancellationToken.None);
        }

        Assert.Equal(1, resolver.CallCount);  // resolver hit once
        Assert.Equal(1, htmlReads);            // page HTML only read on miss
        Assert.Equal(3, dispatched.Count);     // dispatched every time
    }

    [Fact]
    public async Task Cached_arm_dispatch_failure_invalidates_cache_and_re_resolves()
    {
        var resolverArms = new Queue<PageAction>(new PageAction[]
        {
            new PageAction.Click(".stale"),  // first resolution
            new PageAction.Click(".fresh")   // re-resolution after invalidation
        });
        var resolver = new RecordingResolver(_ => resolverArms.Dequeue());
        var coord = new SemanticActCoordinator(resolver, new CapturingLogger());

        // Dispatch 1: resolver returns .stale, dispatch succeeds, cached.
        await coord.DispatchAsync(
            "click",
            getHtmlAsync: _ => Task.FromResult("<html/>"),
            dispatch: (_, _) => Task.CompletedTask,
            CancellationToken.None);
        Assert.Equal(".stale", ((PageAction.Click)coord.TryGetCached("click")!).Selector);

        // Dispatch 2: cached arm throws (selector now missing); coordinator
        // invalidates + re-resolves; resolver returns .fresh; cached.
        var dispatchAttempts = 0;
        await coord.DispatchAsync(
            "click",
            getHtmlAsync: _ => Task.FromResult("<html/>"),
            dispatch: (a, _) =>
            {
                dispatchAttempts++;
                // First attempt (cached .stale) throws; second attempt (.fresh) succeeds.
                if (dispatchAttempts == 1) throw new InvalidOperationException("selector missing");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(2, resolver.CallCount);                    // resolver re-invoked
        Assert.Equal(2, dispatchAttempts);                       // two dispatch attempts on this call
        Assert.Equal(".fresh", ((PageAction.Click)coord.TryGetCached("click")!).Selector);
    }

    [Fact]
    public async Task Resolver_returning_null_throws_SemanticActResolutionException()
    {
        var resolver = new RecordingResolver(_ => null);
        var coord = new SemanticActCoordinator(resolver, new CapturingLogger());

        var ex = await Assert.ThrowsAsync<SemanticActResolutionException>(() =>
            coord.DispatchAsync(
                "do the thing",
                getHtmlAsync: _ => Task.FromResult("<html/>"),
                dispatch: (_, _) => Task.CompletedTask,
                CancellationToken.None));

        Assert.Equal("do the thing", ex.Intent);
        Assert.Equal(0, coord.CacheCount);
    }

    [Fact]
    public async Task Resolver_throwing_throws_SemanticActResolutionException_with_inner()
    {
        var inner = new InvalidOperationException("LLM service down");
        var resolver = new RecordingResolver(_ => throw inner);
        var coord = new SemanticActCoordinator(resolver, new CapturingLogger());

        var ex = await Assert.ThrowsAsync<SemanticActResolutionException>(() =>
            coord.DispatchAsync(
                "click",
                getHtmlAsync: _ => Task.FromResult("<html/>"),
                dispatch: (_, _) => Task.CompletedTask,
                CancellationToken.None));

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public async Task Resolver_returning_SemanticAct_throws_no_loop()
    {
        var resolver = new RecordingResolver(_ => new PageAction.SemanticAct("inner"));
        var coord = new SemanticActCoordinator(resolver, new CapturingLogger());

        var ex = await Assert.ThrowsAsync<SemanticActResolutionException>(() =>
            coord.DispatchAsync(
                "outer",
                getHtmlAsync: _ => Task.FromResult("<html/>"),
                dispatch: (_, _) => Task.CompletedTask,
                CancellationToken.None));

        Assert.Equal("outer", ex.Intent);
        Assert.NotNull(ex.InnerException);
        Assert.Contains("must return a concrete arm", ex.InnerException!.Message);
    }

    [Fact]
    public async Task Freshly_resolved_arm_dispatch_failure_surfaces_and_is_not_cached()
    {
        var dispatchEx = new InvalidOperationException("clicked nothing");
        var resolver = new RecordingResolver(_ => new PageAction.Click(".btn"));
        var coord = new SemanticActCoordinator(resolver, new CapturingLogger());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coord.DispatchAsync(
                "click",
                getHtmlAsync: _ => Task.FromResult("<html/>"),
                dispatch: (_, _) => throw dispatchEx,
                CancellationToken.None));

        Assert.Same(dispatchEx, thrown);          // surfaced as-is, NOT wrapped
        Assert.Equal(0, coord.CacheCount);         // dispatch-failure path doesn't cache
    }

    [Fact]
    public async Task Cancellation_propagates_not_wrapped()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var resolver = new RecordingResolver(_ => throw new OperationCanceledException(cts.Token));
        var coord = new SemanticActCoordinator(resolver, new CapturingLogger());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            coord.DispatchAsync(
                "click",
                getHtmlAsync: _ => Task.FromResult("<html/>"),
                dispatch: (_, _) => Task.CompletedTask,
                cts.Token));
    }

    [Fact]
    public async Task Distinct_intents_keep_distinct_cache_entries()
    {
        var resolver = new RecordingResolver(intent => intent switch
        {
            "click sign in"      => new PageAction.Click(".signin"),
            "wait for the modal" => new PageAction.WaitForSelector(".modal", 5000),
            _ => null
        });
        var coord = new SemanticActCoordinator(resolver, new CapturingLogger());

        await coord.DispatchAsync("click sign in",      _ => Task.FromResult("<html/>"), (_, _) => Task.CompletedTask, CancellationToken.None);
        await coord.DispatchAsync("wait for the modal", _ => Task.FromResult("<html/>"), (_, _) => Task.CompletedTask, CancellationToken.None);
        await coord.DispatchAsync("click sign in",      _ => Task.FromResult("<html/>"), (_, _) => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(2, coord.CacheCount);
        Assert.Equal(2, resolver.CallCount);   // each unique intent resolved once
    }

    // The NullActionResolver is the default registration. It returns null,
    // which the coordinator turns into SemanticActResolutionException — the
    // misconfiguration is visible at the first SemanticAct dispatch.
    [Fact]
    public async Task NullActionResolver_returns_null_and_coordinator_throws()
    {
        var coord = new SemanticActCoordinator(NullActionResolver.Instance, new CapturingLogger());

        await Assert.ThrowsAsync<SemanticActResolutionException>(() =>
            coord.DispatchAsync(
                "click",
                getHtmlAsync: _ => Task.FromResult("<html/>"),
                dispatch: (_, _) => Task.CompletedTask,
                CancellationToken.None));
    }

    // SemanticAct round-trips through the codec exactly like every other arm
    // (ADR-0035) — the intent string is the only field; the resolved arm is
    // intentionally not persisted (would freeze the LLM's selector across
    // crawls, defeating the re-resolve-on-cache-miss recovery path). The
    // WebReaperJson Options aren't public, so the round-trip rides through
    // SerializeJob / DeserializeJob.
    [Fact]
    public void Codec_round_trips_SemanticAct_via_Job()
    {
        var job = new Job(
            "https://example.com",
            ImmutableQueue<LinkPathSelector>.Empty,
            ImmutableQueue<string>.Empty,
            PageType: PageType.Dynamic,
            PageActions: new List<PageAction> { new PageAction.SemanticAct("click sign in") });

        var json = WebReaperJson.SerializeJob(job);
        var roundTripped = WebReaperJson.DeserializeJob(json);

        Assert.Contains("\"semanticAct\"", json);
        Assert.Contains("\"click sign in\"", json);
        var sa = Assert.IsType<PageAction.SemanticAct>(Assert.Single(roundTripped.PageActions!));
        Assert.Equal("click sign in", sa.Intent);
    }

    [Fact]
    public void Builder_SemanticAct_adds_the_arm()
    {
        var actions = new PageActionBuilder().SemanticAct("click sign in").Build();

        var sa = Assert.IsType<PageAction.SemanticAct>(Assert.Single(actions));
        Assert.Equal("click sign in", sa.Intent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Builder_SemanticAct_rejects_blank_intent(string? intent)
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace surfaces ArgumentNullException
        // for null and ArgumentException for empty/whitespace — both fail the
        // builder. ThrowsAny pins "an ArgumentException family throw" without
        // baking the exact subtype into the test.
        Assert.ThrowsAny<ArgumentException>(() => new PageActionBuilder().SemanticAct(intent!));
    }

    // The ScraperEngineBuilder.BuildAsync warning fires when the config carries
    // any SemanticAct AND the default NullActionResolver is still in place.
    // Surfaces the misconfiguration at build time, before the first dispatch
    // throws SemanticActResolutionException at crawl time.
    [Fact]
    public async Task Build_warns_when_SemanticAct_in_config_with_default_resolver()
    {
        var logger = new CapturingLogger();
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };

        await ScraperEngineBuilder
            .CrawlWithBrowser(new[] { "https://example.com" },
                ab => ab.SemanticAct("click sign in").Build())
            .Extract(schema)
            .WithLogger(logger)
            .BuildAsync();

        Assert.Contains(logger.Entries, e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            e.Message.Contains("SemanticAct", StringComparison.OrdinalIgnoreCase) &&
            e.Message.Contains("WithActionResolver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Build_does_not_warn_when_a_resolver_is_registered()
    {
        var logger = new CapturingLogger();
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };

        await ScraperEngineBuilder
            .CrawlWithBrowser(new[] { "https://example.com" },
                ab => ab.SemanticAct("click sign in").Build())
            .Extract(schema)
            .WithLogger(logger)
            .WithActionResolver(new RecordingResolver(_ => new PageAction.Click(".x")))
            .BuildAsync();

        Assert.DoesNotContain(logger.Entries, e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            e.Message.Contains("SemanticAct", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Build_does_not_warn_when_no_SemanticAct_in_config()
    {
        var logger = new CapturingLogger();
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };

        await ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(schema)
            .WithLogger(logger)
            .BuildAsync();

        Assert.DoesNotContain(logger.Entries, e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            e.Message.Contains("SemanticAct", StringComparison.OrdinalIgnoreCase));
    }

    // A recording stub that satisfies IActionResolver synchronously; tests use
    // it to count calls and provide canned responses (including throws).
    private sealed class RecordingResolver : IActionResolver
    {
        private readonly Func<string, PageAction?> _resolve;
        public int CallCount { get; private set; }

        public RecordingResolver(Func<string, PageAction?> resolve) => _resolve = resolve;

        public Task<PageAction?> ResolveAsync(
            string intent, string pageHtml, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_resolve(intent));
        }
    }
}
