namespace WebReaper.Abstractions.Parsers;

public interface ILinkParser
{
    IEnumerable<string> GetLinks(string html, string selector);
}