namespace WebReaper.Abstractions.Loaders;

public interface IDynamicPageLoader
{
    Task<string> Load(string url, string script);
}
