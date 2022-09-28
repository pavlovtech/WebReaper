namespace WebReaper.Abstractions.Loaders;

public interface IPageLoader
{
    Task<string> Load(string url);
}
