using System.Collections.Immutable;

namespace ExoScraper.PageActions;

public class PageActionBuilder
{
    private readonly List<PageAction> _pageActions = new();

    public PageActionBuilder Click(string selector)
    {
        _pageActions.Add(new(PageActionType.Click, selector));
        return this;
    }

    public PageActionBuilder Wait(int milliseconds)
    {
        _pageActions.Add(new(PageActionType.Wait, milliseconds));
        return this;
    }

    public PageActionBuilder ScrollToEnd()
    {
        _pageActions.Add(new(PageActionType.ScrollToEnd));
        return this;
    }

    public PageActionBuilder RepeatWithDelay(int times, int milliseconds)
    {
        var lastEl = _pageActions[^1];

        _pageActions.AddRange(
            Enumerable.Range(1, times)
                .Select(_ => new PageAction[] 
                {
                    lastEl,
                    new PageAction(PageActionType.Wait, milliseconds) 
                })
                .SelectMany(x => x));

        return this;
    }

    public PageActionBuilder RepeatAndWaitForNetworkIdle(int times)
    {

        var lastEl = _pageActions[^1];

        _pageActions.AddRange(
            Enumerable.Range(1, times)
                .Select(_ => new PageAction[]
                {
                    lastEl,
                    new PageAction(PageActionType.WaitForNetworkIdle)
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
        _pageActions.Add(new(PageActionType.EvaluateExpression, expression));
        return this;
    }

    public PageActionBuilder WaitForSelector(string selector, int timeout)
    {
        _pageActions.Add(new(PageActionType.WaitForSelector, selector, timeout));
        return this;
    }

    public PageActionBuilder WaitForNetworkIdle()
    {
        _pageActions.Add(new(PageActionType.WaitForSelector));
        return this;
    }

    public List<PageAction> Build() => _pageActions;
}