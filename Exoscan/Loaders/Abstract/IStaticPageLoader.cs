namespace Exoscan.Loaders.Abstract;

public interface IStaticPageLoader
{
    Task<string> Load(string url);
}
