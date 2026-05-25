using WebReaper.AI.Llm;

namespace WebReaper.AI;

/// <summary>
/// Policy bag for <see cref="UseAiRegistration.UseAi(WebReaper.Builders.ScraperEngineBuilder, Microsoft.Extensions.AI.IChatClient, AiOptions?)"/>
/// and its agent sibling (ADR-0064). One options record carries a global tuple
/// of LLM defaults (<see cref="Model"/>, <see cref="Temperature"/>,
/// <see cref="MaxResponseTokens"/>, <see cref="MarkdownPreClean"/>) plus
/// optional per-role overrides. The <see cref="Policy"/> arm picks which
/// adapters get wired.
/// <para>
/// <b>Per-role override discipline.</b> When a per-role record (e.g.
/// <see cref="Extractor"/>) is <c>null</c>, every adapter for that role
/// inherits the global defaults — the consumer wrote one tuple and got
/// consistent everywhere. When a per-role record is non-null, <em>every</em>
/// non-nullable scalar on that record acts as an override (because C# cannot
/// distinguish "explicit 0.0" from "didn't set" on a positional record's
/// float). So <c>new AiOptions(Brain: new LlmAgentBrainOptions())</c> sets the
/// brain's <c>Temperature</c> to the per-role default (0), not the global
/// override. If you want a per-role override of <em>just one field</em>, pass
/// the rest as repeated values: <c>new LlmAgentBrainOptions(Model: globalModel,
/// Temperature: 0.5f)</c>. Nullable fields (<see cref="LlmExtractorOptions.Model"/>,
/// <see cref="LlmExtractorOptions.SystemPrompt"/>) follow the "null means
/// inherit" convention regardless.
/// </para>
/// </summary>
/// <param name="Model">Default model id (flows down to every per-role record
/// whose <c>Model</c> is null). <c>null</c> means defer to the chat client's
/// default — most consumers configure the model at the
/// <c>IChatClient</c> level and leave this null.</param>
/// <param name="Temperature">Default sampling temperature. Default 0 — every
/// AI role in the policy is a deterministic task (extraction, action
/// resolution, brain decisions); non-zero is opt-in.</param>
/// <param name="MaxResponseTokens">Default response token cap. Default 4096
/// — matches the extractor's ceiling. The resolver and brain wrappers cap
/// their own per-role defaults below this when their per-role record is null
/// (e.g. resolver caps at <c>Math.Min(MaxResponseTokens, 512)</c> — small
/// JSON object). The per-role record overrides when non-null.</param>
/// <param name="MarkdownPreClean">Default Markdown pre-clean (extractor only).
/// Defaults to <c>true</c> for ~10× token savings; opt-out for sites where
/// chrome stripping risks losing data. Only the extractor role consumes this
/// field.</param>
/// <param name="Extractor">Optional per-role override for the LLM content
/// extractor (<see cref="LlmContentExtractor"/>). When null, synthesised from
/// the global defaults.</param>
/// <param name="Resolver">Optional per-role override for the LLM action
/// resolver (<see cref="LlmActionResolver"/>). When null, synthesised from
/// the global defaults.</param>
/// <param name="Brain">Optional per-role override for the LLM agent brain
/// (<see cref="LlmAgentBrain"/>). When null, synthesised from the global
/// defaults. Ignored on the scraper builder.</param>
/// <param name="Repairer">Optional per-role override for the LLM selector
/// repairer (<see cref="LlmSelectorRepairer"/>). Reuses the
/// <see cref="LlmExtractorOptions"/> shape — the repairer's knobs are
/// the extractor's. When null, synthesised from the global defaults.</param>
/// <param name="Inferrer">Optional per-role override for the LLM schema
/// inferrer (<see cref="LlmSchemaInferrer"/>) — consumed by
/// <see cref="AiPolicyMode.Inferred"/> on the scraper (ADR-0068). When
/// null, synthesised from the global defaults; the inferrer's own
/// 32 000-char content cap + 1024-token response cap defaults apply
/// (the inferrer's response is a small JSON object; the global
/// <see cref="MaxResponseTokens"/> does not flow into it). Ignored on
/// the agent builder — the agent's brain proposes schemas per
/// decision, so a separate inferrer is structurally redundant.</param>
/// <param name="Policy">Which canned configuration to wire. Default
/// <see cref="AiPolicyMode.Recommended"/> — the firecrawl-shaped
/// deterministic-first + LLM-rescue posture.</param>
/// <param name="CachePolicy">Global system-prompt caching policy (ADR-0065).
/// Default <see cref="WebReaper.AI.Llm.CachePolicy.Hinted"/> — the AI-native
/// ethos (cheaper by default when safe). Flows down to per-role records
/// when they are <c>null</c>; per-role records that ARE non-null carry
/// their own <c>CachePolicy</c> (default
/// <see cref="WebReaper.AI.Llm.CachePolicy.Default"/>) — matches the
/// existing all-or-nothing per-role-wins convention on this record (see
/// the type-level XML doc). To override a non-null per-role record's
/// cache policy, set the field on the per-role record explicitly.</param>
public sealed record AiOptions(
    string? Model = null,
    float Temperature = 0.0f,
    int MaxResponseTokens = 4096,
    bool MarkdownPreClean = true,
    LlmExtractorOptions? Extractor = null,
    LlmActionResolverOptions? Resolver = null,
    LlmAgentBrainOptions? Brain = null,
    LlmExtractorOptions? Repairer = null,
    LlmSchemaInferrerOptions? Inferrer = null,
    AiPolicyMode Policy = AiPolicyMode.Recommended,
    CachePolicy CachePolicy = CachePolicy.Hinted)
{
    /// <summary>
    /// Synthesise the effective <see cref="LlmExtractorOptions"/> for the
    /// content-extractor role. When <see cref="Extractor"/> is null, every
    /// field comes from the global defaults. When non-null, the per-role
    /// record wins for every field — see the override discipline note on
    /// the type-level XML doc.
    /// </summary>
    internal LlmExtractorOptions ResolveExtractorOptions()
        => Extractor is null
            ? new LlmExtractorOptions(
                Model: Model,
                UseMarkdownPreClean: MarkdownPreClean,
                MaxTokens: MaxResponseTokens,
                Temperature: Temperature,
                SystemPrompt: null,
                CachePolicy: CachePolicy)
            : Extractor with { CachePolicy = Extractor.CachePolicy ?? CachePolicy };

    /// <summary>
    /// Synthesise the effective <see cref="LlmExtractorOptions"/> for the
    /// selector-repairer role. Same merge rule as
    /// <see cref="ResolveExtractorOptions"/> — the repairer reuses the
    /// extractor's options shape.
    /// </summary>
    internal LlmExtractorOptions ResolveRepairerOptions()
        => Repairer is null
            ? new LlmExtractorOptions(
                Model: Model,
                UseMarkdownPreClean: MarkdownPreClean,
                MaxTokens: MaxResponseTokens,
                Temperature: Temperature,
                SystemPrompt: null,
                CachePolicy: CachePolicy)
            : Repairer with { CachePolicy = Repairer.CachePolicy ?? CachePolicy };

    /// <summary>
    /// Synthesise the effective <see cref="LlmActionResolverOptions"/> for
    /// the action-resolver role. When <see cref="Resolver"/> is null, every
    /// field comes from the global defaults, with the resolver's own
    /// 512-token output cap honoured (the resolver's response is a small
    /// JSON object — capping at <c>Math.Min(MaxResponseTokens, 512)</c>
    /// keeps cost down without surprising consumers who explicitly raised
    /// the global cap for the extractor).
    /// </summary>
    internal LlmActionResolverOptions ResolveResolverOptions()
        => Resolver is null
            ? new LlmActionResolverOptions(
                Model: Model,
                Temperature: Temperature,
                MaxResponseTokens: Math.Min(MaxResponseTokens, 512),
                MaxHtmlChars: 32_000,
                SystemPrompt: null,
                CachePolicy: CachePolicy)
            : Resolver with { CachePolicy = Resolver.CachePolicy ?? CachePolicy };

    /// <summary>
    /// Synthesise the effective <see cref="LlmAgentBrainOptions"/> for the
    /// brain role. When <see cref="Brain"/> is null, every field comes from
    /// the global defaults, with the brain's own 1024-token output cap
    /// honoured (the brain's response is a small JSON object naming the
    /// decision arm, with room for a multi-field Extract schema).
    /// </summary>
    internal LlmAgentBrainOptions ResolveBrainOptions()
        => Brain is null
            ? new LlmAgentBrainOptions(
                Model: Model,
                Temperature: Temperature,
                MaxResponseTokens: Math.Min(MaxResponseTokens, 1024),
                SystemPrompt: null,
                CachePolicy: CachePolicy)
            : Brain with { CachePolicy = Brain.CachePolicy ?? CachePolicy };

    /// <summary>
    /// Synthesise the effective <see cref="LlmSchemaInferrerOptions"/> for
    /// the schema-inferrer role (ADR-0068). When <see cref="Inferrer"/> is
    /// null, the inferrer's own role-specific defaults apply for the
    /// response-shape fields (<c>MaxContentChars = 32 000</c>,
    /// <c>MaxResponseTokens = 1024</c>) — the global
    /// <see cref="MaxResponseTokens"/> does NOT flow in (the inferrer's
    /// response is a small JSON object; matches the resolver's
    /// <c>Math.Min(MaxResponseTokens, 512)</c> pattern). Global
    /// <see cref="Model"/> / <see cref="Temperature"/> /
    /// <see cref="MarkdownPreClean"/> / <see cref="CachePolicy"/> do flow.
    /// When non-null, the per-role record wins for every field — see the
    /// override discipline note on the type-level XML doc.
    /// </summary>
    internal LlmSchemaInferrerOptions ResolveInferrerOptions()
        => Inferrer is null
            ? new LlmSchemaInferrerOptions(
                Model: Model,
                UseMarkdownPreClean: MarkdownPreClean,
                MaxContentChars: 32_000,
                MaxResponseTokens: 1024,
                Temperature: Temperature,
                SystemPrompt: null,
                CachePolicy: CachePolicy)
            : Inferrer with { CachePolicy = Inferrer.CachePolicy ?? CachePolicy };
}

/// <summary>
/// Canned configurations for <see cref="UseAiRegistration.UseAi(WebReaper.Builders.ScraperEngineBuilder, Microsoft.Extensions.AI.IChatClient, AiOptions?)"/>
/// (ADR-0064). Mutually exclusive (enum, not flags) — each mode is a
/// distinct <em>recommendation</em>, not a feature set to compose. À la
/// carte composition lives on the existing <c>WithLlm*</c> methods.
/// </summary>
public enum AiPolicyMode
{
    /// <summary>
    /// Deterministic-first + LLM rescue (the firecrawl-shaped default).
    /// <para>
    /// <b>Scraper:</b> wires <see cref="LlmExtractorRegistration.WithLlmFallback"/>
    /// (deterministic primary, LLM fallback on validation failure) +
    /// <see cref="LlmExtractorRegistration.WithLlmSelfHealing"/>
    /// (LLM repairs selectors on the deterministic primary's failure) +
    /// <see cref="LlmActionResolverRegistration.WithLlmActionResolver"/>
    /// (so <c>SemanticAct</c> works).
    /// </para>
    /// <para>
    /// <b>Agent:</b> wires <see cref="LlmAgentBrainRegistration.WithLlmBrain"/>
    /// (required) + <see cref="LlmExtractorRegistration.WithLlmFallback"/>
    /// + <see cref="LlmActionResolverRegistration.WithLlmActionResolver"/>.
    /// </para>
    /// </summary>
    Recommended,

    /// <summary>
    /// LLM-primary extraction (the "trust the model" path).
    /// <para>
    /// <b>Scraper:</b> wires <see cref="LlmExtractorRegistration.WithLlmExtractor"/>
    /// (LLM replaces the deterministic fold) +
    /// <see cref="LlmExtractorRegistration.WithLlmSelfHealing"/> +
    /// <see cref="LlmActionResolverRegistration.WithLlmActionResolver"/>.
    /// </para>
    /// <para>
    /// <b>Agent:</b> wires <see cref="LlmAgentBrainRegistration.WithLlmBrain"/>
    /// + <see cref="LlmExtractorRegistration.WithLlmExtractor"/> +
    /// <see cref="LlmActionResolverRegistration.WithLlmActionResolver"/>.
    /// </para>
    /// </summary>
    LlmPrimary,

    /// <summary>
    /// Extraction-only — no action resolver. For callers that want AI
    /// on extraction but not on actions.
    /// <para>
    /// <b>Scraper:</b> wires <see cref="LlmExtractorRegistration.WithLlmFallback"/>
    /// only.
    /// </para>
    /// <para>
    /// <b>Agent:</b> wires <see cref="LlmAgentBrainRegistration.WithLlmBrain"/>
    /// (required) + <see cref="LlmExtractorRegistration.WithLlmFallback"/>.
    /// </para>
    /// </summary>
    ExtractionOnly,

    /// <summary>
    /// Explicit escape hatch — wires nothing on the scraper; wires only the
    /// brain on the agent (the agent is structurally useless without one).
    /// <c>.UseAi(client, new AiOptions(Policy: AiPolicyMode.None))</c> is
    /// the test-harness path for bespoke compositions: AI is "available"
    /// (the chat client is registered for the brain on the agent) but the
    /// caller chooses every other adapter à la carte via <c>WithLlm*</c>.
    /// </summary>
    None,

    /// <summary>
    /// Runtime schema inference — the "extract structured data without a
    /// schema" path (ADR-0067 + ADR-0068). The fifth strategy on the
    /// extraction surface; closes the firecrawl parity gap for
    /// schema-less structured extraction.
    /// <para>
    /// <b>Scraper:</b> wires
    /// <see cref="LlmSchemaInferrerRegistration.WithLlmSchemaInferrer"/>
    /// (so the wrapper composed at <c>BuildAsync</c> for
    /// <c>.ExtractInferred(...)</c> has a real inferrer) +
    /// <see cref="LlmActionResolverRegistration.WithLlmActionResolver"/>
    /// (the orthogonal action surface — useful regardless of extraction
    /// strategy). Mutually exclusive with
    /// <see cref="Recommended"/> / <see cref="LlmPrimary"/> /
    /// <see cref="ExtractionOnly"/> — those register an
    /// <see cref="WebReaper.Core.Parser.Abstract.IContentExtractor"/>
    /// that would shadow the
    /// <see cref="WebReaper.Core.Parser.Concrete.LearnedSchemaContentExtractor"/>
    /// wrapper. The consumer-facing one-liner:
    /// <code>
    /// var engine = await ScraperEngineBuilder
    ///     .Crawl("https://shop.com/products")
    ///     .ExtractInferred(goal: "product details")
    ///     .UseAi(chatClient, new AiOptions(Policy: AiPolicyMode.Inferred))
    ///     .WriteToConsole()
    ///     .BuildAsync();
    /// </code>
    /// </para>
    /// <para>
    /// <b>Agent:</b> not supported. The agent's brain proposes its own
    /// schemas in <c>AgentDecision.Extract(schema)</c>; a separate
    /// inferrer arm is structurally redundant on that builder.
    /// <c>.UseAi(agentBuilder, new AiOptions(Policy: Inferred))</c>
    /// throws <see cref="System.ArgumentOutOfRangeException"/> with an
    /// actionable message pointing at <see cref="Recommended"/> /
    /// <see cref="LlmPrimary"/>.
    /// </para>
    /// </summary>
    Inferred
}
