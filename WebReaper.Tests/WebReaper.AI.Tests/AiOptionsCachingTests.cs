using WebReaper.AI.Llm;

namespace WebReaper.AI.Tests;

// ADR-0065: contract tests for AiOptions.CachePolicy global flow and
// per-role nullable override semantics. The per-role records'
// CachePolicy is nullable (null = inherit from AiOptions.CachePolicy);
// AiOptions.Resolve* helpers materialise the effective value into the
// returned per-role record so the adapter ctor sees a non-null choice.
public class AiOptionsCachingTests
{
    // ---- Defaults ------------------------------------------------------------

    [Fact]
    public void AiOptions_default_CachePolicy_is_Hinted()
    {
        // The AI-native ethos: cheaper by default when safe.
        var opts = new AiOptions();
        Assert.Equal(CachePolicy.Hinted, opts.CachePolicy);
    }

    [Fact]
    public void Per_role_options_default_CachePolicy_is_null_meaning_inherit()
    {
        Assert.Null(new LlmExtractorOptions().CachePolicy);
        Assert.Null(new LlmActionResolverOptions().CachePolicy);
        Assert.Null(new LlmAgentBrainOptions().CachePolicy);
    }

    // ---- Resolve* synth-from-global path (per-role record absent) -----------

    [Fact]
    public void ResolveExtractorOptions_uses_global_CachePolicy_when_no_per_role()
    {
        var opts = new AiOptions(CachePolicy: CachePolicy.Hinted);
        Assert.Equal(CachePolicy.Hinted, opts.ResolveExtractorOptions().CachePolicy);
    }

    [Fact]
    public void ResolveRepairerOptions_uses_global_CachePolicy_when_no_per_role()
    {
        var opts = new AiOptions(CachePolicy: CachePolicy.Hinted);
        Assert.Equal(CachePolicy.Hinted, opts.ResolveRepairerOptions().CachePolicy);
    }

    [Fact]
    public void ResolveResolverOptions_uses_global_CachePolicy_when_no_per_role()
    {
        var opts = new AiOptions(CachePolicy: CachePolicy.Hinted);
        Assert.Equal(CachePolicy.Hinted, opts.ResolveResolverOptions().CachePolicy);
    }

    [Fact]
    public void ResolveBrainOptions_uses_global_CachePolicy_when_no_per_role()
    {
        var opts = new AiOptions(CachePolicy: CachePolicy.Hinted);
        Assert.Equal(CachePolicy.Hinted, opts.ResolveBrainOptions().CachePolicy);
    }

    // ---- Resolve* per-field inheritance (per-role record present, CachePolicy null) --

    [Fact]
    public void ResolveExtractorOptions_inherits_global_when_per_role_CachePolicy_is_null()
    {
        // ADR-0065 §6: nullable CachePolicy on per-role; null = inherit.
        var opts = new AiOptions(
            CachePolicy: CachePolicy.Hinted,
            Extractor: new LlmExtractorOptions()); // CachePolicy null by default
        var resolved = opts.ResolveExtractorOptions();
        Assert.Equal(CachePolicy.Hinted, resolved.CachePolicy);
    }

    [Fact]
    public void ResolveBrainOptions_inherits_global_when_per_role_CachePolicy_is_null()
    {
        var opts = new AiOptions(
            CachePolicy: CachePolicy.Hinted,
            Brain: new LlmAgentBrainOptions());
        Assert.Equal(CachePolicy.Hinted, opts.ResolveBrainOptions().CachePolicy);
    }

    [Fact]
    public void ResolveResolverOptions_inherits_global_when_per_role_CachePolicy_is_null()
    {
        var opts = new AiOptions(
            CachePolicy: CachePolicy.Hinted,
            Resolver: new LlmActionResolverOptions());
        Assert.Equal(CachePolicy.Hinted, opts.ResolveResolverOptions().CachePolicy);
    }

    // ---- Resolve* per-role override (per-role record present, CachePolicy non-null) --

    [Fact]
    public void ResolveExtractorOptions_per_role_explicit_CachePolicy_wins()
    {
        var opts = new AiOptions(
            CachePolicy: CachePolicy.Hinted,
            Extractor: new LlmExtractorOptions(CachePolicy: CachePolicy.Default));
        var resolved = opts.ResolveExtractorOptions();
        // The per-role explicit Default overrides the global Hinted.
        Assert.Equal(CachePolicy.Default, resolved.CachePolicy);
    }

    [Fact]
    public void ResolveBrainOptions_per_role_explicit_CachePolicy_wins()
    {
        var opts = new AiOptions(
            CachePolicy: CachePolicy.Default,
            Brain: new LlmAgentBrainOptions(CachePolicy: CachePolicy.Hinted));
        Assert.Equal(CachePolicy.Hinted, opts.ResolveBrainOptions().CachePolicy);
    }

    [Fact]
    public void ResolveResolverOptions_per_role_explicit_CachePolicy_wins()
    {
        var opts = new AiOptions(
            CachePolicy: CachePolicy.Hinted,
            Resolver: new LlmActionResolverOptions(CachePolicy: CachePolicy.Default));
        Assert.Equal(CachePolicy.Default, opts.ResolveResolverOptions().CachePolicy);
    }

    [Fact]
    public void ResolveRepairerOptions_per_role_explicit_CachePolicy_wins()
    {
        var opts = new AiOptions(
            CachePolicy: CachePolicy.Hinted,
            Repairer: new LlmExtractorOptions(CachePolicy: CachePolicy.Default));
        Assert.Equal(CachePolicy.Default, opts.ResolveRepairerOptions().CachePolicy);
    }

    // ---- Per-role record preserves other fields when overriding CachePolicy --

    [Fact]
    public void ResolveExtractorOptions_with_per_role_preserves_other_per_role_fields()
    {
        // The `with` clause in Resolve* must update only CachePolicy; the
        // other fields on the per-role record must survive untouched.
        var opts = new AiOptions(
            CachePolicy: CachePolicy.Hinted,
            Extractor: new LlmExtractorOptions(
                Model: "explicit-model",
                Temperature: 0.42f,
                MaxTokens: 999,
                UseMarkdownPreClean: false,
                SystemPrompt: "explicit"));
        var resolved = opts.ResolveExtractorOptions();

        Assert.Equal("explicit-model", resolved.Model);
        Assert.Equal(0.42f, resolved.Temperature);
        Assert.Equal(999, resolved.MaxTokens);
        Assert.False(resolved.UseMarkdownPreClean);
        Assert.Equal("explicit", resolved.SystemPrompt);
        // CachePolicy inherited from global.
        Assert.Equal(CachePolicy.Hinted, resolved.CachePolicy);
    }

    // ---- À la carte (no Resolve*) -------------------------------------------

    [Fact]
    public void Per_role_options_with_null_CachePolicy_works_a_la_carte()
    {
        // À la carte construction does NOT go through Resolve*; the
        // adapter's ctor handles the null fallback (treats null as
        // CachePolicy.Default — see LlmExtractorOptions.CachePolicy
        // XML doc). This test pins the per-role record's nullable
        // shape; the adapter-level fallback is exercised by
        // CachePolicyTests via Default-policy behaviour.
        var explicitHinted = new LlmExtractorOptions(CachePolicy: CachePolicy.Hinted);
        Assert.Equal(CachePolicy.Hinted, explicitHinted.CachePolicy);

        var explicitDefault = new LlmExtractorOptions(CachePolicy: CachePolicy.Default);
        Assert.Equal(CachePolicy.Default, explicitDefault.CachePolicy);
    }
}
