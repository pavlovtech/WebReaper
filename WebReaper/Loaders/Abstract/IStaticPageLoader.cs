namespace WebReaper.Loaders.Concrete;

public interface IStaticPageLoader
{
    Task<string> Load(string url);
}
