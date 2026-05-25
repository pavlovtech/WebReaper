// ADR-0067 + ADR-0068 + ADR-0069 — the v10.0.0 schema-inference dock on
// display. Each sub-command exercises one public API on the new surface:
//
//   dotnet run --project Examples/WebReaper.SchemaInferenceShowcase -- alacarte
//   dotnet run --project Examples/WebReaper.SchemaInferenceShowcase -- useai
//   dotnet run --project Examples/WebReaper.SchemaInferenceShowcase -- reinfer
//
// Network: `alacarte` and `useai` hit `example.com` (a single page;
// the inference call goes to a deterministic in-process StubChatClient
// so no API key is required). `reinfer` constructs the wrapper
// directly with synthetic stubs and never touches the network.

using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.AI;
using WebReaper.AI.Llm;
using WebReaper.Builders;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;
using WebReaper.SchemaInferenceShowcase;

string command = args.Length == 0 ? "alacarte" : args[0];

switch (command)
{
    case "alacarte": await AlaCarte();    break;
    case "useai":    await UseAiPolicy(); break;
    case "reinfer":  await ReInfer();     break;
    default:
        Console.Error.WriteLine($"Unknown subcommand '{command}'. " +
            "Expected one of: alacarte, useai, reinfer.");
        Environment.Exit(2);
        break;
}

// ADR-0067 — `.ExtractInferred(goal?)` is the third `ICrawlSeed`
// terminal. The inferrer proposes a Schema on the first page; the
// deterministic fold consumes it on every subsequent page. À la carte
// via `.WithLlmSchemaInferrer(client, options?)`.
static async Task AlaCarte()
{
    // Stub returns a flat field-name → CSS-selector map. In production
    // this is one round-trip to OpenAI / Anthropic / Ollama / etc.
    var stub = new StubChatClient(
        """{"fields": {"title": "h1", "summary": "p"}}""");

    var engine = await ScraperEngineBuilder
        .Crawl("https://example.com/")
        .ExtractInferred(goal: "page title and summary")
        .WithLlmSchemaInferrer(stub)
        .WriteToConsole()
        .PageCrawlLimit(1)
        .BuildAsync();

    await engine.RunAsync();
    Console.WriteLine(
        $"(à la carte: inferred a schema once via {stub.Calls} LLM call; " +
        "every subsequent page would run the deterministic fold against " +
        "the cached schema.)");
}

// ADR-0068 — `AiPolicyMode.Inferred` makes `.ExtractInferred(goal?)`
// a one-liner via `.UseAi(client, AiOptions(Policy: Inferred))`. The
// policy wires `WithLlmSchemaInferrer + WithLlmActionResolver`
// (the orthogonal action surface). Mutually exclusive with
// Recommended / LlmPrimary / ExtractionOnly — those register an
// IContentExtractor that would shadow LearnedSchemaContentExtractor.
static async Task UseAiPolicy()
{
    var stub = new StubChatClient(
        """{"fields": {"title": "h1", "summary": "p"}}""");

    var engine = await ScraperEngineBuilder
        .Crawl("https://example.com/")
        .ExtractInferred(goal: "page title and summary")
        .UseAi(stub, new AiOptions(Policy: AiPolicyMode.Inferred))
        .WriteToConsole()
        .PageCrawlLimit(1)
        .BuildAsync();

    await engine.RunAsync();
    Console.WriteLine(
        "(one-line policy: .UseAi(client, Inferred) wires the inferrer " +
        "AND the action resolver in one call. The synthesised inferrer " +
        "options inherit CachePolicy.Hinted from AiOptions for the " +
        "Anthropic cost benefit.)");
}

// ADR-0069 — validator-driven re-inference. The LearnedSchemaContentExtractor
// wrapper consults the registered ISchemaValidator (ADR-0062 default:
// SchemaSatisfiedValidator) on every inner-extractor output; N
// consecutive invalid verdicts drop the cached inferred schema and
// trigger re-inference on the next call. Default N = 3; opt out with
// ReInferAfterFailures = 0; cost cap via MaxReInferencesPerInstance.
//
// Demonstrated by constructing the wrapper directly with scripted
// stubs — easier to see the count progression than driving a real
// multi-page crawl. The unit tests (LearnedSchemaReInferenceTests)
// pin the same mechanics across the full matrix.
static async Task ReInfer()
{
    // Stub inferrer cycles through two schemas: the first one points
    // at selectors that produce empty content (triggers validator
    // failures); the second one is correct (passes validation).
    var inferrer = new ScriptedInferrer(
        new Schema { new SchemaElement("title", "#bad-selector") },
        new Schema { new SchemaElement("title", "h1") });

    // Inner extractor: empty-content for the first cached schema,
    // valid content for the second. Drives the validator's verdict.
    var inner = new ScriptedExtractor();

    // Default behaviour — ReInferAfterFailures = 3.
    var wrapper = new LearnedSchemaContentExtractor(
        inferrer, inner,
        goal: "page title",
        logger: NullLogger.Instance,
        validator: SchemaSatisfiedValidator.Instance,
        reInferAfterFailures: 3,
        maxReInferencesPerInstance: int.MaxValue);

    Console.WriteLine("--- default (ReInferAfterFailures: 3) ---");
    for (var i = 1; i <= 7; i++)
    {
        var result = await wrapper.ExtractAsync($"<page{i}/>", null);
        var title = result["title"]?.GetValue<string>() ?? "(empty)";
        Console.WriteLine(
            $"  page {i}: title='{title}' " +
            $"(inferrer.Calls={inferrer.Calls}, " +
            $"wrapper.ReInferencesUsed={wrapper.ReInferencesUsed})");
    }
    await wrapper.DisposeAsync();

    // Opt-out — ReInferAfterFailures = 0 preserves the ADR-0067 v1
    // trust-the-cache behaviour. The wrapper never drops the cached
    // schema regardless of validator verdicts.
    Console.WriteLine();
    Console.WriteLine("--- opt-out (ReInferAfterFailures: 0) ---");
    var inferrerOptOut = new ScriptedInferrer(
        new Schema { new SchemaElement("title", "#bad-selector") },
        new Schema { new SchemaElement("title", "h1") });
    var innerOptOut = new ScriptedExtractor();
    var wrapperOptOut = new LearnedSchemaContentExtractor(
        inferrerOptOut, innerOptOut,
        goal: "page title",
        validator: SchemaSatisfiedValidator.Instance,
        reInferAfterFailures: 0);
    for (var i = 1; i <= 5; i++)
        await wrapperOptOut.ExtractAsync($"<page{i}/>", null);
    Console.WriteLine(
        $"  after 5 always-failing pages: " +
        $"inferrer.Calls={inferrerOptOut.Calls}, " +
        $"wrapper.ReInferencesUsed={wrapperOptOut.ReInferencesUsed} " +
        "(strict ADR-0067 v1 trust-the-cache preserved)");
    await wrapperOptOut.DisposeAsync();

    // Cost cap — MaxReInferencesPerInstance bounds total LLM spend.
    Console.WriteLine();
    Console.WriteLine("--- cost cap (MaxReInferencesPerInstance: 1) ---");
    var inferrerCap = new ScriptedInferrer(
        new Schema { new SchemaElement("title", "#bad") },
        new Schema { new SchemaElement("title", "#also-bad") },
        new Schema { new SchemaElement("title", "h1") });
    var innerCap = new ScriptedExtractor();
    var wrapperCap = new LearnedSchemaContentExtractor(
        inferrerCap, innerCap,
        goal: "page title",
        validator: SchemaSatisfiedValidator.Instance,
        reInferAfterFailures: 3,
        maxReInferencesPerInstance: 1);
    for (var i = 1; i <= 10; i++)
        await wrapperCap.ExtractAsync($"<page{i}/>", null);
    Console.WriteLine(
        $"  after 10 always-failing pages: " +
        $"inferrer.Calls={inferrerCap.Calls}, " +
        $"wrapper.ReInferencesUsed={wrapperCap.ReInferencesUsed} " +
        "(cap honoured — second drop kept stale schema, logged at Warning)");
    await wrapperCap.DisposeAsync();

    Console.WriteLine();
    Console.WriteLine(
        "(re-inference: same proposer-validator wedge as ADR-0046/0047/0050/0051, " +
        "now applied to schema lifecycle. The wrapper's ReInferencesUsed property " +
        "is the cost-observability surface.)");
}

// Stub inferrer that cycles through a list of schemas — first call
// returns the first; each subsequent call returns the next (or the
// last forever).
internal sealed class ScriptedInferrer : ISchemaInferrer
{
    private readonly Schema[] _schemas;
    private int _calls;
    public int Calls => _calls;

    public ScriptedInferrer(params Schema[] schemas) => _schemas = schemas;

    public Task<Schema> InferAsync(string document, string? goal = null,
        CancellationToken cancellationToken = default)
    {
        var idx = Math.Min(_calls, _schemas.Length - 1);
        Interlocked.Increment(ref _calls);
        return Task.FromResult(_schemas[idx]);
    }
}

// Stub extractor whose output is purely a function of the cached
// schema's first selector — selectors starting with `#` produce
// empty content (drives the validator to "invalid"); anything else
// produces "Example Domain" (validator "valid"). Stateless; the demo
// flips behaviour by having the inferrer swap to a non-`#` selector
// after the wrapper drops the cache.
internal sealed class ScriptedExtractor : IContentExtractor
{
    public Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        var firstChild = schema?.Children.FirstOrDefault() as SchemaElement;
        var selector = firstChild?.Selector ?? string.Empty;
        var produces = selector.StartsWith('#') ? string.Empty : "Example Domain";
        return Task.FromResult(new JsonObject { ["title"] = produces });
    }
}
