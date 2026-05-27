using System.Text.Json;
using WebReaper.AI.Tools;
using WebReaper.Domain.PageActions;

namespace WebReaper.AI.Tests;

// ADR-0060 amendment (2026-05-28): per-arm tool projection in
// PageActionTools. These tests pin each arm's FromArguments contract
// directly — the brain (LlmAgentBrain) and resolver (LlmActionResolver)
// both go through these factories, but the existing integration tests
// for those adapters exercise the OUTER tool-call path. These tests
// pin the INNER contract: what each arm accepts, what it rejects, and
// what shape it emits on failure (the FailureReason string the brain
// composes into its audit-trail Stop reason).
public class PageActionToolsTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    // ---- Name constants -----------------------------------------------------

    [Fact]
    public void Tool_names_match_the_pre_amendment_constants()
    {
        // The brain's and resolver's switch labels referenced literal
        // strings before the amendment ("ActClick", "ActWait", ...).
        // Now they reference the Name constants. Pin the constant
        // values so a rename here doesn't silently break tool-call
        // matching against models that learned the old names.
        Assert.Equal("ActClick", PageActionTools.Click.Name);
        Assert.Equal("ActWait", PageActionTools.Wait.Name);
        Assert.Equal("ActWaitForSelector", PageActionTools.WaitForSelector.Name);
        Assert.Equal("ActWaitForNetworkIdle", PageActionTools.WaitForNetworkIdle.Name);
        Assert.Equal("ActScrollToEnd", PageActionTools.ScrollToEnd.Name);
        Assert.Equal("ActEvaluate", PageActionTools.EvaluateExpression.Name);
        Assert.Equal("ActSemanticAct", PageActionTools.SemanticAct.Name);
    }

    [Fact]
    public void Descriptor_name_matches_the_arm_name_constant()
    {
        // The HandRolledAIFunction name should match the Name constant
        // verbatim — the LLM sees the tool by name, the parser
        // dispatches by name, the two must agree.
        Assert.Equal(PageActionTools.Click.Name, PageActionTools.Click.Descriptor.Name);
        Assert.Equal(PageActionTools.Wait.Name, PageActionTools.Wait.Descriptor.Name);
        Assert.Equal(PageActionTools.WaitForSelector.Name, PageActionTools.WaitForSelector.Descriptor.Name);
        Assert.Equal(PageActionTools.WaitForNetworkIdle.Name, PageActionTools.WaitForNetworkIdle.Descriptor.Name);
        Assert.Equal(PageActionTools.ScrollToEnd.Name, PageActionTools.ScrollToEnd.Descriptor.Name);
        Assert.Equal(PageActionTools.EvaluateExpression.Name, PageActionTools.EvaluateExpression.Descriptor.Name);
        Assert.Equal(PageActionTools.SemanticAct.Name, PageActionTools.SemanticAct.Descriptor.Name);
    }

    // ---- Click --------------------------------------------------------------

    [Fact]
    public void Click_FromArguments_constructs_the_arm_on_valid_input()
    {
        var result = PageActionTools.Click.FromArguments(
            Parse("""{ "selector": ".btn" }"""));
        var click = Assert.IsType<PageAction.Click>(result.Value);
        Assert.Equal(".btn", click.Selector);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Click_FromArguments_fails_on_missing_selector()
    {
        var result = PageActionTools.Click.FromArguments(Parse("""{}"""));
        Assert.Null(result.Value);
        Assert.Equal("missing 'selector'", result.FailureReason);
    }

    [Fact]
    public void Click_FromArguments_fails_on_whitespace_selector()
    {
        // The brain's pre-amendment contract treated whitespace as
        // missing (string.IsNullOrWhiteSpace) — pin that here.
        var result = PageActionTools.Click.FromArguments(
            Parse("""{ "selector": "   " }"""));
        Assert.Null(result.Value);
        Assert.Equal("missing 'selector'", result.FailureReason);
    }

    // ---- Wait ---------------------------------------------------------------

    [Fact]
    public void Wait_FromArguments_uses_the_ms_value()
    {
        var result = PageActionTools.Wait.FromArguments(
            Parse("""{ "ms": 250 }"""));
        var wait = Assert.IsType<PageAction.Wait>(result.Value);
        Assert.Equal(250, wait.Milliseconds);
    }

    [Fact]
    public void Wait_FromArguments_defaults_to_zero_when_ms_absent()
    {
        // Pre-amendment behaviour: ActWait without an integer was
        // treated as a zero-length wait (TryGetInt(args, "ms") ?? 0).
        // Preserve the lenient default so a model that omits the
        // argument doesn't silently fail the dispatch.
        var result = PageActionTools.Wait.FromArguments(Parse("""{}"""));
        var wait = Assert.IsType<PageAction.Wait>(result.Value);
        Assert.Equal(0, wait.Milliseconds);
    }

    // ---- WaitForSelector ----------------------------------------------------

    [Fact]
    public void WaitForSelector_FromArguments_with_explicit_timeout()
    {
        var result = PageActionTools.WaitForSelector.FromArguments(
            Parse("""{ "selector": ".modal", "timeoutMs": 5000 }"""));
        var wfs = Assert.IsType<PageAction.WaitForSelector>(result.Value);
        Assert.Equal(".modal", wfs.Selector);
        Assert.Equal(5000, wfs.TimeoutMs);
    }

    [Fact]
    public void WaitForSelector_FromArguments_defaults_timeout_when_omitted()
    {
        // Pre-amendment default: TryGetInt(args, "timeoutMs") ?? 30_000.
        var result = PageActionTools.WaitForSelector.FromArguments(
            Parse("""{ "selector": ".modal" }"""));
        var wfs = Assert.IsType<PageAction.WaitForSelector>(result.Value);
        Assert.Equal(30_000, wfs.TimeoutMs);
    }

    [Fact]
    public void WaitForSelector_FromArguments_fails_on_missing_selector()
    {
        var result = PageActionTools.WaitForSelector.FromArguments(
            Parse("""{ "timeoutMs": 5000 }"""));
        Assert.Null(result.Value);
        Assert.Equal("missing 'selector'", result.FailureReason);
    }

    // ---- Argument-less arms (WaitForNetworkIdle, ScrollToEnd) ---------------

    [Fact]
    public void WaitForNetworkIdle_FromArguments_always_succeeds()
    {
        // The arm carries no required arguments; FromArguments should
        // succeed regardless of input shape (even if args is empty or
        // contains stray keys the LLM emitted).
        Assert.IsType<PageAction.WaitForNetworkIdle>(
            PageActionTools.WaitForNetworkIdle.FromArguments(Parse("""{}""")).Value);
        Assert.IsType<PageAction.WaitForNetworkIdle>(
            PageActionTools.WaitForNetworkIdle.FromArguments(Parse("""{ "stray": 1 }""")).Value);
    }

    [Fact]
    public void ScrollToEnd_FromArguments_always_succeeds()
    {
        Assert.IsType<PageAction.ScrollToEnd>(
            PageActionTools.ScrollToEnd.FromArguments(Parse("""{}""")).Value);
    }

    // ---- EvaluateExpression -------------------------------------------------

    [Fact]
    public void EvaluateExpression_FromArguments_on_valid_input()
    {
        var result = PageActionTools.EvaluateExpression.FromArguments(
            Parse("""{ "expression": "document.title" }"""));
        var eval = Assert.IsType<PageAction.EvaluateExpression>(result.Value);
        Assert.Equal("document.title", eval.Expression);
    }

    [Fact]
    public void EvaluateExpression_FromArguments_fails_on_missing_expression()
    {
        var result = PageActionTools.EvaluateExpression.FromArguments(Parse("""{}"""));
        Assert.Null(result.Value);
        Assert.Equal("missing 'expression'", result.FailureReason);
    }

    // ---- SemanticAct --------------------------------------------------------

    [Fact]
    public void SemanticAct_FromArguments_on_valid_input()
    {
        var result = PageActionTools.SemanticAct.FromArguments(
            Parse("""{ "intent": "click sign in" }"""));
        var act = Assert.IsType<PageAction.SemanticAct>(result.Value);
        Assert.Equal("click sign in", act.Intent);
    }

    [Fact]
    public void SemanticAct_FromArguments_fails_on_missing_intent()
    {
        var result = PageActionTools.SemanticAct.FromArguments(Parse("""{}"""));
        Assert.Null(result.Value);
        Assert.Equal("missing 'intent'", result.FailureReason);
    }
}
