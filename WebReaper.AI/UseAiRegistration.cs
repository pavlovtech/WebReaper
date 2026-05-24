using Microsoft.Extensions.AI;
using WebReaper.Builders;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Core.Parser.Abstract;

namespace WebReaper.AI;

/// <summary>
/// The headline one-line AI enablement extensions (ADR-0064). One method per
/// builder; one <see cref="IChatClient"/>; an optional <see cref="AiOptions"/>
/// to pick the policy and per-role overrides. Sugar over the existing
/// per-role <c>WithLlm*</c> methods (those remain for fine-tuning) — calling
/// <c>.UseAi(client)</c> is exactly equivalent to threading the same chat
/// client through the per-role methods named by the chosen
/// <see cref="AiPolicyMode"/>.
/// </summary>
public static class UseAiRegistration
{
    /// <summary>
    /// One-line AI enablement for the scraper builder (ADR-0064). Wires
    /// the per-role LLM adapters named by <paramref name="options"/>'s
    /// <see cref="AiOptions.Policy"/>:
    /// <list type="bullet">
    /// <item><description><see cref="AiPolicyMode.Recommended"/> (default) —
    /// <see cref="LlmExtractorRegistration.WithLlmFallback"/> +
    /// <see cref="LlmExtractorRegistration.WithLlmSelfHealing"/> +
    /// <see cref="LlmActionResolverRegistration.WithLlmActionResolver"/>.</description></item>
    /// <item><description><see cref="AiPolicyMode.LlmPrimary"/> —
    /// <see cref="LlmExtractorRegistration.WithLlmExtractor"/> +
    /// <see cref="LlmExtractorRegistration.WithLlmSelfHealing"/> +
    /// <see cref="LlmActionResolverRegistration.WithLlmActionResolver"/>.</description></item>
    /// <item><description><see cref="AiPolicyMode.ExtractionOnly"/> —
    /// <see cref="LlmExtractorRegistration.WithLlmFallback"/> only.</description></item>
    /// <item><description><see cref="AiPolicyMode.None"/> — wires nothing
    /// (escape hatch for tests / bespoke compositions).</description></item>
    /// </list>
    /// </summary>
    /// <param name="builder">The scraper builder.</param>
    /// <param name="chatClient">The <see cref="IChatClient"/> threaded into
    /// every wired adapter. The satellite makes no per-adapter client choice;
    /// to use a different model per role, call <c>.UseAi(brainClient)</c>
    /// then override per role via <c>WithLlm*</c>.</param>
    /// <param name="options">Optional policy + per-role overrides. Defaults
    /// to <c>new AiOptions()</c> — <see cref="AiPolicyMode.Recommended"/>
    /// with the global defaults.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="chatClient"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="options"/>'s <see cref="AiOptions.Policy"/> is not a
    /// defined <see cref="AiPolicyMode"/> value.</exception>
    public static ScraperEngineBuilder UseAi(
        this ScraperEngineBuilder builder,
        IChatClient chatClient,
        AiOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);

        options ??= new AiOptions();
        var extractorOpts = options.ResolveExtractorOptions();
        var resolverOpts  = options.ResolveResolverOptions();
        var repairerOpts  = options.ResolveRepairerOptions();

        return options.Policy switch
        {
            AiPolicyMode.Recommended    => builder
                .WithLlmFallback(chatClient, extractorOpts)
                .WithLlmSelfHealing(chatClient, repairerOpts)
                .WithLlmActionResolver(chatClient, resolverOpts),
            AiPolicyMode.LlmPrimary     => builder
                .WithLlmExtractor(chatClient, extractorOpts)
                .WithLlmSelfHealing(chatClient, repairerOpts)
                .WithLlmActionResolver(chatClient, resolverOpts),
            AiPolicyMode.ExtractionOnly => builder
                .WithLlmFallback(chatClient, extractorOpts),
            AiPolicyMode.None           => builder,
            _ => throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Policy,
                $"Unknown {nameof(AiPolicyMode)} value."),
        };
    }

    /// <summary>
    /// One-line AI enablement for the agent builder (ADR-0064). The brain
    /// is wired unconditionally on every mode (the agent is structurally
    /// useless without one — ADR-0051), then the extractor and action
    /// resolver per <paramref name="options"/>'s <see cref="AiOptions.Policy"/>.
    /// Behaviour is symmetric with the scraper overload now that
    /// <see cref="AgentEngineBuilder.WithFallbackExtractor"/> and
    /// <see cref="AgentEngineBuilder.WithSelfHealing"/> exist:
    /// <list type="bullet">
    /// <item><description><see cref="AiPolicyMode.Recommended"/> (default) —
    /// brain + deterministic primary with LLM fallback + LLM selector
    /// repair + LLM action resolver. Same firecrawl-shaped triple as the
    /// scraper.</description></item>
    /// <item><description><see cref="AiPolicyMode.LlmPrimary"/> — brain +
    /// LLM content extractor (replaces the deterministic fold) + LLM
    /// selector repair + LLM action resolver.</description></item>
    /// <item><description><see cref="AiPolicyMode.ExtractionOnly"/> — brain
    /// + deterministic primary with LLM fallback; no self-heal, no
    /// action resolver.</description></item>
    /// <item><description><see cref="AiPolicyMode.None"/> — brain only
    /// (escape hatch for tests / bespoke compositions; the brain is the
    /// agent's structural requirement).</description></item>
    /// </list>
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="chatClient">The <see cref="IChatClient"/> threaded into
    /// every wired adapter (brain, extractor, repairer, action resolver).</param>
    /// <param name="options">Optional policy + per-role overrides. Defaults
    /// to <c>new AiOptions()</c> — <see cref="AiPolicyMode.Recommended"/>
    /// with the global defaults.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="chatClient"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="options"/>'s <see cref="AiOptions.Policy"/> is not a
    /// defined <see cref="AiPolicyMode"/> value.</exception>
    public static AgentEngineBuilder UseAi(
        this AgentEngineBuilder builder,
        IChatClient chatClient,
        AiOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);

        options ??= new AiOptions();
        var brainOpts     = options.ResolveBrainOptions();
        var extractorOpts = options.ResolveExtractorOptions();
        var resolverOpts  = options.ResolveResolverOptions();
        var repairerOpts  = options.ResolveRepairerOptions();

        // Brain is always wired (the agent is structurally useless without one
        // — ADR-0051 §Decision §5; AgentEngineBuilder.BuildAsync throws when
        // the brain is still the null sentinel). The None arm wires the brain
        // and nothing else.
        builder.WithLlmBrain(chatClient, brainOpts);

        switch (options.Policy)
        {
            case AiPolicyMode.Recommended:
                // Deterministic primary + LLM fallback + LLM self-heal +
                // LLM action resolver — the firecrawl-shaped triple,
                // mirror of the scraper-side wiring.
                builder.WithFallbackExtractor(new LlmContentExtractor(chatClient, extractorOpts));
                builder.WithSelfHealing(new LlmSelectorRepairer(chatClient, repairerOpts));
                builder.WithActionResolver(new LlmActionResolver(chatClient, resolverOpts));
                break;
            case AiPolicyMode.LlmPrimary:
                // LLM-primary extraction + LLM self-heal + LLM action
                // resolver — mirror of the scraper-side wiring.
                builder.WithContentExtractor(new LlmContentExtractor(chatClient, extractorOpts));
                builder.WithSelfHealing(new LlmSelectorRepairer(chatClient, repairerOpts));
                builder.WithActionResolver(new LlmActionResolver(chatClient, resolverOpts));
                break;
            case AiPolicyMode.ExtractionOnly:
                builder.WithFallbackExtractor(new LlmContentExtractor(chatClient, extractorOpts));
                break;
            case AiPolicyMode.None:
                // Brain already wired above; nothing else.
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.Policy,
                    $"Unknown {nameof(AiPolicyMode)} value.");
        }
        return builder;
    }
}
