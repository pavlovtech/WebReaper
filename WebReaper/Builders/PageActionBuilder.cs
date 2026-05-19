using WebReaper.Domain.PageActions;

namespace WebReaper.Builders;

public class PageActionBuilder
{
    private readonly List<PageAction> _pageActions = new();

    public PageActionBuilder Click(string selector)
    {
        _pageActions.Add(new PageAction(PageActionType.Click, selector));
        return this;
    }

    public PageActionBuilder Wait(int milliseconds)
    {
        _pageActions.Add(new PageAction(PageActionType.Wait, milliseconds));
        return this;
    }

    public PageActionBuilder ScrollToEnd()
    {
        _pageActions.Add(new PageAction(PageActionType.ScrollToEnd));
        return this;
    }

    // The Repeat* methods replay the previously-added action. With no prior
    // action there is nothing to replay: surface that as a clear builder
    // misuse instead of the raw ArgumentOutOfRangeException from _pageActions[^1].
    private PageAction LastActionToRepeat(string method) =>
        _pageActions.Count > 0
            ? _pageActions[^1]
            : throw new InvalidOperationException(
                $"{method} can only be called after at least one page action has been added — there is no action to repeat.");

    public PageActionBuilder RepeatWithDelay(int times, int milliseconds)
    {
        var lastEl = LastActionToRepeat(nameof(RepeatWithDelay));

        _pageActions.AddRange(
            Enumerable.Range(1, times)
                .Select(_ => new[]
                {
                    lastEl,
                    new(PageActionType.Wait, milliseconds)
                })
                .SelectMany(x => x));

        return this;
    }

    public PageActionBuilder RepeatAndWaitForNetworkIdle(int times)
    {
        var lastEl = LastActionToRepeat(nameof(RepeatAndWaitForNetworkIdle));

        _pageActions.AddRange(
            Enumerable.Range(1, times)
                .Select(_ => new[]
                {
                    lastEl,
                    new(PageActionType.WaitForNetworkIdle)
                })
                .SelectMany(x => x));

        return this;
    }

    public PageActionBuilder Repeat(int times)
    {
        var lastEl = LastActionToRepeat(nameof(Repeat));
        _pageActions.AddRange(Enumerable.Range(1, times).Select(_ => lastEl));
        return this;
    }

    public PageActionBuilder EvaluateExpression(string expression)
    {
        _pageActions.Add(new PageAction(PageActionType.EvaluateExpression, expression));
        return this;
    }

    public PageActionBuilder WaitForSelector(string selector, int timeout)
    {
        _pageActions.Add(new PageAction(PageActionType.WaitForSelector, selector, timeout));
        return this;
    }

    public PageActionBuilder WaitForNetworkIdle()
    {
        _pageActions.Add(new PageAction(PageActionType.WaitForNetworkIdle));
        return this;
    }

    public List<PageAction> Build()
    {
        return _pageActions;
    }
}