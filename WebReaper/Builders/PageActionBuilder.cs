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

    public PageActionBuilder RepeatWithDelay(int times, int milliseconds)
    {
        var lastEl = _pageActions[^1];

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
        var lastEl = _pageActions[^1];

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
        _pageActions.AddRange(Enumerable.Range(1, times).Select(_ => _pageActions[^1]));
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
        _pageActions.Add(new PageAction(PageActionType.WaitForSelector));
        return this;
    }

    public List<PageAction> Build()
    {
        return _pageActions;
    }
}