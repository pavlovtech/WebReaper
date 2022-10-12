namespace WebReaper.Parser.Abstract;

public interface ILinkParser
{
    IEnumerable<string> GetLinks(Uri baseUrl, string html, string selector);
}