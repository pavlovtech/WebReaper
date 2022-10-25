namespace WebReaper.Parser.Abstract;

public interface ILinkParser
{
    List<string> GetLinks(Uri baseUrl, string html, string selector);
}