using WebReaper.Builders;
using WebReaper.Domain.PageActions;

namespace WebReaper.UnitTests;

// #61: PageActionBuilder.WaitForNetworkIdle() tagged the action
// WaitForSelector (a copy-paste typo) — silently producing a different,
// unimplemented action. These pin every single-action builder method to its
// matching PageActionType so a future copy-paste typo fails a fast unit test
// instead of silently at the Puppeteer dispatcher.
public class PageActionBuilderTests
{
    [Fact]
    public void WaitForNetworkIdle_tags_the_action_WaitForNetworkIdle()
    {
        var actions = new PageActionBuilder().WaitForNetworkIdle().Build();

        var action = Assert.Single(actions);
        Assert.Equal(PageActionType.WaitForNetworkIdle, action.Type);
    }

    [Fact]
    public void Every_single_action_method_tags_the_action_with_its_matching_type()
    {
        Assert.Equal(PageActionType.Click,
            Assert.Single(new PageActionBuilder().Click("sel").Build()).Type);
        Assert.Equal(PageActionType.Wait,
            Assert.Single(new PageActionBuilder().Wait(10).Build()).Type);
        Assert.Equal(PageActionType.ScrollToEnd,
            Assert.Single(new PageActionBuilder().ScrollToEnd().Build()).Type);
        Assert.Equal(PageActionType.EvaluateExpression,
            Assert.Single(new PageActionBuilder().EvaluateExpression("expr").Build()).Type);
        Assert.Equal(PageActionType.WaitForSelector,
            Assert.Single(new PageActionBuilder().WaitForSelector("sel", 100).Build()).Type);
        Assert.Equal(PageActionType.WaitForNetworkIdle,
            Assert.Single(new PageActionBuilder().WaitForNetworkIdle().Build()).Type);
    }

    [Fact]
    public void RepeatAndWaitForNetworkIdle_repeats_the_seed_then_a_WaitForNetworkIdle()
    {
        // Sibling of the bug and a copy-paste hot spot: the action inserted
        // between repeats must be WaitForNetworkIdle, not WaitForSelector.
        var actions = new PageActionBuilder()
            .ScrollToEnd()
            .RepeatAndWaitForNetworkIdle(2)
            .Build();

        // seed ScrollToEnd, then 2x [ScrollToEnd, WaitForNetworkIdle]
        Assert.Equal(5, actions.Count);
        Assert.Equal(PageActionType.WaitForNetworkIdle, actions[2].Type);
        Assert.Equal(PageActionType.WaitForNetworkIdle, actions[4].Type);
    }
}
