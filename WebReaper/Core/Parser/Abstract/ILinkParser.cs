namespace WebReaper.Core.Parser.Abstract;

public interface ILinkParser
{
    Task<List<string>> GetLinksAsync(Uri baseUrl, string html, string selector);
}