using WebReaper.Domain.PageActions;

namespace WebReaper.Core.Loaders.Abstract;

public interface IBrowserPageLoader
{
    Task<string> Load(string url, List<PageAction>? pageActions = null, bool headless = true);
}