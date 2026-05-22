using WebReaper.Builders;
using WebReaper.Domain.PageActions;

namespace WebReaper.UnitTests;

// ADR-0035: PageAction is a closed sum — each builder method constructs a
// typed arm record, not a (PageActionType, object[]) pair. The #61 class of
// bug (WaitForNetworkIdle() mis-tagged WaitForSelector — a copy-paste typo on
// the enum discriminant, silently producing a different, unimplemented action)
// is now structurally impossible: the arm IS the type, so a mis-tag is a
// different record type with different fields — a compile error. These pin
// each builder method to its arm and typed fields, and the Repeat* combinators.
public class PageActionBuilderTests
{
    [Fact]
    public void Every_single_action_method_builds_its_matching_typed_arm()
    {
        Assert.Equal(".x",
            Assert.IsType<PageAction.Click>(
                Assert.Single(new PageActionBuilder().Click(".x").Build())).Selector);

        Assert.Equal(250,
            Assert.IsType<PageAction.Wait>(
                Assert.Single(new PageActionBuilder().Wait(250).Build())).Milliseconds);

        Assert.IsType<PageAction.ScrollToEnd>(
            Assert.Single(new PageActionBuilder().ScrollToEnd().Build()));

        Assert.Equal("document.title",
            Assert.IsType<PageAction.EvaluateExpression>(
                Assert.Single(new PageActionBuilder().EvaluateExpression("document.title").Build())).Expression);

        var wfs = Assert.IsType<PageAction.WaitForSelector>(
            Assert.Single(new PageActionBuilder().WaitForSelector(".ready", 5000).Build()));
        Assert.Equal((".ready", 5000), (wfs.Selector, wfs.TimeoutMs));

        Assert.IsType<PageAction.WaitForNetworkIdle>(
            Assert.Single(new PageActionBuilder().WaitForNetworkIdle().Build()));
    }

    // 8.0.0 hardening: the Repeat* methods replay the last-added action via
    // _pageActions[^1]. Called first they used to throw a bare
    // ArgumentOutOfRangeException; pin the builder-misuse InvalidOperationException
    // so the diagnostic ("there is no action to repeat") can't silently regress.
    [Fact]
    public void Repeat_methods_throw_a_clear_error_when_called_before_any_action()
    {
        Assert.Throws<InvalidOperationException>(
            () => new PageActionBuilder().Repeat(2));
        Assert.Throws<InvalidOperationException>(
            () => new PageActionBuilder().RepeatWithDelay(2, 100));
        Assert.Throws<InvalidOperationException>(
            () => new PageActionBuilder().RepeatAndWaitForNetworkIdle(2));
    }

    [Fact]
    public void Repeat_replays_the_last_action_n_more_times()
    {
        var actions = new PageActionBuilder()
            .ScrollToEnd()
            .Repeat(3)
            .Build();

        // seed ScrollToEnd, then 3 more copies of it
        Assert.Equal(4, actions.Count);
        Assert.All(actions, a => Assert.IsType<PageAction.ScrollToEnd>(a));
    }

    [Fact]
    public void RepeatAndWaitForNetworkIdle_repeats_the_seed_then_a_WaitForNetworkIdle()
    {
        // The action inserted between repeats must be WaitForNetworkIdle.
        var actions = new PageActionBuilder()
            .ScrollToEnd()
            .RepeatAndWaitForNetworkIdle(2)
            .Build();

        // seed ScrollToEnd, then 2x [ScrollToEnd, WaitForNetworkIdle]
        Assert.Equal(5, actions.Count);
        Assert.IsType<PageAction.WaitForNetworkIdle>(actions[2]);
        Assert.IsType<PageAction.WaitForNetworkIdle>(actions[4]);
    }
}
