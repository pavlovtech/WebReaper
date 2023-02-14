using System.Collections.Immutable;
using ExoScraper.PageActions;

namespace ExoScraper.Loaders.Abstract;

public interface IBrowserPageLoader
{
    Task<string> Load(string url, List<PageAction>? pageActions = null);
}
