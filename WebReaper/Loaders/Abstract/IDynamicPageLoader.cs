namespace WebReaper.Loaders.Abstract;

public interface IBrowserPageLoader
{
    Task<string> Load(string url, string? script);
}
