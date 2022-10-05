namespace WebReaper.Abstractions.Loaders;

public interface IStaticPageLoader
{
    Task<string> Load(string url);
}
