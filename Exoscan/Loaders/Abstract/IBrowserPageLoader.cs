using System.Collections.Immutable;
using Exoscan.PageActions;

namespace Exoscan.Loaders.Abstract;

public interface IBrowserPageLoader
{
    Task<string> Load(string url, ImmutableQueue<PageAction>? pageActions = null);
}
