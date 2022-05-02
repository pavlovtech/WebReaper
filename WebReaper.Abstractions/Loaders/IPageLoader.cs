namespace WebReaper.Abstractions.Loaders.PageLoader;

public interface IPageLoader
{
    Task<string> Load(string url);
}
