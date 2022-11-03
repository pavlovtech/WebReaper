using System.Collections.Immutable;
using WebReaper.PageActions;

namespace WebReaper.Loaders.Abstract;

public interface IBrowserPageLoader
{
    Task<string> Load(string url, ImmutableQueue<PageAction>? PageActions = null);
}
