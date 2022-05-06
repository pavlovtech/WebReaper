namespace WebReaper.LinkTracker.Abstract
{
    public interface ICrawledLinkTracker
    {
        Task AddVisitedLinkAsync(string siteUrl, string visitedLink);
        Task<IEnumerable<string>> GetVisitedLinksAsync(string siteName);
    }
}