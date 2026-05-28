using System.Collections.Concurrent;
using Anthropic.SDK;
using Microsoft.Extensions.AI;
using OpenAI;
using WebReaper.AI;
using WebReaper.Builders;
using WebReaper.IntegrationTests.Fixtures;
using WebReaper.Sinks.Models;
using WebReaper.TestServer;
using Xunit;
using Xunit.Abstractions;

namespace WebReaper.IntegrationTests;

/// <summary>
/// AI-feature coverage with REAL LLMs, exercising BOTH stock
/// Microsoft.Extensions.AI providers — Anthropic.SDK's MessagesEndpoint and
/// Microsoft.Extensions.AI.OpenAI. Each provider test is gated on its own key;
/// when a key is absent that test passes vacuously (the repo's CloakBrowser
/// idiom), so an unkeyed run is green. A provider that rejects on billing
/// (HTTP 429 insufficient_quota) is soft-skipped too — the request reached the
/// API, so the integration path is exercised; the account just has no credit.
///
/// Single-version graph: Anthropic.SDK 5.10.0 only works on abstractions 10.3.0
/// (the last with HostedMcpServerTool.AuthorizationToken), and
/// Microsoft.Extensions.AI.OpenAI 10.3.0 targets that same abstractions — so
/// both real providers coexist on one stable (non-preview) version.
///
/// The page is the deterministic local <c>/static</c> fixture, so only the LLM
/// is non-deterministic: we assert the known page content ("Widget Pro 3000")
/// lands in the emitted record — the model's job is to locate it, not to author
/// prose. The model id is threaded through the WebReaper option record
/// (ChatOptions.ModelId), per the satellite's contract.
/// </summary>
[Collection("LocalSite")]
[Trait("Category", "Llm")]
public sealed class AiIntegrationTests
{
    private readonly LocalTestSite _site;
    private readonly ITestOutputHelper _output;

    public AiIntegrationTests(LocalSiteFixture fixture, ITestOutputHelper output)
    {
        _site = fixture.Site;
        _output = output;
    }

    private static (IChatClient Client, string Model)? AnthropicOrNull()
    {
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        return string.IsNullOrWhiteSpace(key)
            ? null
            : (new AnthropicClient(key).Messages, "claude-haiku-4-5-20251001");
    }

    private static (IChatClient Client, string Model)? OpenAiOrNull()
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        return string.IsNullOrWhiteSpace(key)
            ? null
            : (new OpenAIClient(key).GetChatClient("gpt-4o-mini").AsIChatClient(), "gpt-4o-mini");
    }

    /// <summary>Crawl <c>/static</c> with LLM-inferred extraction and assert the
    /// known product name made it into the record.</summary>
    private async Task AssertInferredExtraction(IChatClient client, string model)
    {
        var records = new ConcurrentQueue<ParsedData>();

        await using (var engine = await ScraperEngineBuilder
            .Crawl(_site.Url("/static"))
            .ExtractInferred(goal: "the product title and price")
            .WithLlmSchemaInferrer(client, new LlmSchemaInferrerOptions(Model: model))
            .Subscribe(records.Enqueue)
            .WithLogger(new TestOutputLogger(_output))
            .PageCrawlLimit(1)
            .StopWhenAllLinksProcessed()
            .BuildAsync())
        {
            await engine.RunAsync();
        }

        var rec = Assert.Single(records);
        Assert.Contains("Widget Pro 3000", rec.Data.ToJsonString());
    }

    private static bool IsQuotaExhausted(Exception ex) =>
        ex.Message.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("429");

    /// <summary>Run <paramref name="body"/>, soft-skipping when the provider
    /// rejects on billing (429 insufficient_quota): the request reached the API
    /// — the integration path is exercised up to the send — the account just has
    /// no credit, so treat it like a missing key, not a code failure. Genuine
    /// errors (other 4xx, 5xx, parse failures) still fail the test.</summary>
    private async Task RunOrSkipOnQuota(Func<Task> body)
    {
        try
        {
            await body();
        }
        catch (Exception ex) when (IsQuotaExhausted(ex))
        {
            _output.WriteLine($"LLM quota exhausted — API reached, account unfunded; soft-skip. {ex.Message}");
        }
    }

    [Fact]
    public async Task Anthropic_schema_inference_extracts_known_fields()
    {
        if (AnthropicOrNull() is not { } c) return;   // no key → vacuous pass
        await RunOrSkipOnQuota(() => AssertInferredExtraction(c.Client, c.Model));
    }

    [Fact]
    public async Task OpenAi_schema_inference_extracts_known_fields()
    {
        if (OpenAiOrNull() is not { } c) return;       // no key → vacuous pass
        await RunOrSkipOnQuota(() => AssertInferredExtraction(c.Client, c.Model));
    }

    [Fact]
    public async Task UseAi_inferred_one_liner_extracts_known_fields()
    {
        if ((AnthropicOrNull() ?? OpenAiOrNull()) is not { } c) return;

        await RunOrSkipOnQuota(async () =>
        {
            var records = new ConcurrentQueue<ParsedData>();

            await using (var engine = await ScraperEngineBuilder
                .Crawl(_site.Url("/static"))
                .ExtractInferred(goal: "the product title and price")
                .UseAi(c.Client, new AiOptions(Policy: AiPolicyMode.Inferred, Model: c.Model))
                .Subscribe(records.Enqueue)
                .WithLogger(new TestOutputLogger(_output))
                .PageCrawlLimit(1)
                .StopWhenAllLinksProcessed()
                .BuildAsync())
            {
                await engine.RunAsync();
            }

            var rec = Assert.Single(records);
            Assert.Contains("Widget Pro 3000", rec.Data.ToJsonString());
        });
    }
}
