namespace ExoScraper.Loaders.Abstract;

public interface IStaticPageLoader
{
    Task<string> Load(string url);
}
