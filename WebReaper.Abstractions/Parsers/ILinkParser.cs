public interface ILinkParser
{
    IEnumerable<string> GetLinks(string html, string selector);
}