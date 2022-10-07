namespace WebReaper.Core.Parser.Abstract;

public interface ILinkParser
{
    IEnumerable<string> GetLinks(string html, string selector);
}