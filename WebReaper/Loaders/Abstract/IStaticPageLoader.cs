namespace WebReaper.Loaders.Abstract;

public interface IStaticPageLoader
{
    Task<string> Load(string url);
}
