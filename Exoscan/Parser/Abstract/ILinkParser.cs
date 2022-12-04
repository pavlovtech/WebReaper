namespace Exoscan.Parser.Abstract;

public interface ILinkParser
{
    Task<List<string>> GetLinksAsync(Uri baseUrl, string html, string selector);
}