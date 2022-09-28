namespace WebReaper.Abstractions.LinkTracker
{
    public interface ICrawledLinkTracker
    {
        Task AddVisitedLinkAsync(string siteUrl, string visitedLink);
        Task<IEnumerable<string>> GetVisitedLinksAsync(string siteName);
        Task<IEnumerable<string>> GetNotVisitedLinks(string siteUrl, IEnumerable<string> links);
        Task<long> GetVisitedLinksCount(string siteUrl);
    }
}