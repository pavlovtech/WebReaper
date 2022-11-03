using System.Collections.Immutable;

namespace WebReaper.PageActions
{
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
            _pageActions.Add(new(PageActionType.RepeatWithDelay, times, milliseconds));
            return this;
        }

        public PageActionBuilder Repeat(int times)
        {
            _pageActions.Add(new(PageActionType.Repeat, times));
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

        public ImmutableQueue<PageAction> Build()
        {
            return ImmutableQueue.Create(_pageActions.ToArray());
        }
    }
}
