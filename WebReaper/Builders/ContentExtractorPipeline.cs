using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;

namespace WebReaper.Builders;

/// <summary>
/// The shared "current extractor + current validator + the wrappers that
/// compose them" module both <see cref="ScraperEngineBuilder"/> and
/// <see cref="AgentEngineBuilder"/> embed (ADR-0072, 2026-05-28).
/// <para>
/// Before ADR-0072 each builder carried near-identical bodies for
/// <c>WithContentExtractor</c>, <c>WithFallbackExtractor</c>,
/// <c>WithSelfHealing</c>, and <c>WithSchemaValidator</c> — the same
/// "read current extractor or default, wrap with
/// <see cref="ExtractionRouter"/> / <see cref="SelfHealingContentExtractor"/>,
/// read current validator" logic written twice. This module owns the
/// state and the composition once; both builders forward.
/// </para>
/// <para>
/// Scope is deliberate: only the four extractor-cluster methods, where
/// the wrapping logic is non-trivial and the validator state is read
/// across calls. Sinks (<c>AddSink</c> / <c>WriteToConsole</c>),
/// processors (<c>AddProcessor</c>), and the action resolver
/// (<c>WithActionResolver</c>) stay on each builder — those are 1-line
/// list adds or property sets with no shared composition logic; the
/// deletion test for sharing them fails (moving them here would
/// relocate complexity, not concentrate it).
/// </para>
/// <para>
/// Internal — the two outer builders compose with this module by HAS-A,
/// not IS-A; the ADR-0025 "two seams, not one bug" outer separation
/// holds. The shared inner module is named, not the outer.
/// </para>
/// </summary>
internal sealed class ContentExtractorPipeline
{
    // ADR-0072: a getter, not a captured logger. The outer builder's
    // Logger / _logger is mutated by WithLogger after construction; the
    // wrappers (ExtractionRouter / SelfHealingContentExtractor) must
    // receive whichever logger is current at the time the With* method
    // runs — same semantics as the pre-refactor code (which read the
    // outer Logger property at call time).
    private readonly Func<ILogger> _logger;
    private IContentExtractor? _extractor;
    private ISchemaValidator? _validator;

    public ContentExtractorPipeline(Func<ILogger> loggerProvider)
    {
        ArgumentNullException.ThrowIfNull(loggerProvider);
        _logger = loggerProvider;
    }

    /// <summary>The current content extractor, or <c>null</c> if none has
    /// been registered. The two outer builders' <c>BuildAsync</c> resolve
    /// the default via <see cref="GetExtractorOrDefault"/> at build time.</summary>
    public IContentExtractor? Extractor => _extractor;

    /// <summary>The current schema validator, or <c>null</c> if none has
    /// been registered — wrappers receive <c>null</c> and fall back to
    /// their own constructor default (<see cref="SchemaSatisfiedValidator"/>),
    /// preserving the "one obvious default" invariant.</summary>
    public ISchemaValidator? Validator => _validator;

    /// <summary>Replace the current extractor with <paramref name="extractor"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="extractor"/> is null.</exception>
    public void WithContentExtractor(IContentExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        _extractor = extractor;
    }

    /// <summary>
    /// Wrap the currently-registered (or default <see cref="SchemaFold{TNode}"/>)
    /// extractor with an <see cref="ExtractionRouter"/> (ADR-0046): on a
    /// validation failure (per the registered validator, or the default
    /// <see cref="SchemaSatisfiedValidator"/>), escalate to
    /// <paramref name="fallback"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="fallback"/> is null.</exception>
    public void WithFallbackExtractor(IContentExtractor fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        var primary = GetExtractorOrDefault();
        _extractor = new ExtractionRouter(primary, fallback, _validator, _logger());
    }

    /// <summary>
    /// Wrap the currently-registered (or default <see cref="SchemaFold{TNode}"/>)
    /// extractor with a <see cref="SelfHealingContentExtractor"/>
    /// (ADR-0047): on a failed deterministic pass, ask
    /// <paramref name="repairer"/> for a patched Schema, re-validate, and
    /// cache the patch for every subsequent page of the run.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="repairer"/> is null.</exception>
    public void WithSelfHealing(ISelectorRepairer repairer)
    {
        ArgumentNullException.ThrowIfNull(repairer);
        var primary = GetExtractorOrDefault();
        _extractor = new SelfHealingContentExtractor(primary, repairer, _validator, _logger());
    }

    /// <summary>Register a custom <see cref="ISchemaValidator"/> — consulted
    /// by <see cref="WithFallbackExtractor"/> / <see cref="WithSelfHealing"/>
    /// at the time they compose their wrapper. Call <em>before</em> those
    /// methods; the wrapper composes against whatever validator is
    /// registered at that moment.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="validator"/> is null.</exception>
    public void WithSchemaValidator(ISchemaValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
    }

    /// <summary>Return the current extractor or build the default
    /// <see cref="SchemaFold{TNode}"/> over the AngleSharp/CSS backend.
    /// Used by the wrapper methods AND by the outer builders at
    /// <c>BuildAsync</c> time to resolve the final extractor passed into
    /// the engine.
    /// <para>
    /// Memoising: when <see cref="Extractor"/> is null, the first call
    /// constructs a default <see cref="SchemaFold{TNode}"/> AND commits
    /// it to the pipeline (<c>_extractor ??= ...</c>). Every subsequent
    /// call returns the same instance. This matches the pre-ADR-0072
    /// <c>SpiderBuilder.Build</c> behaviour, which assigned via
    /// <c>??=</c> on first use; it also prevents <c>BuildAsync</c> from
    /// allocating multiple SchemaFold instances when the pipeline is
    /// read more than once (e.g. once for the
    /// <see cref="WebReaper.Core.Parser.Concrete.LearnedSchemaContentExtractor"/>
    /// inner extractor and again for the spider sync).
    /// </para></summary>
    public IContentExtractor GetExtractorOrDefault() =>
        _extractor ??= new SchemaFold<IParentNode>(new AngleSharpSchemaBackend(), _logger());
}
