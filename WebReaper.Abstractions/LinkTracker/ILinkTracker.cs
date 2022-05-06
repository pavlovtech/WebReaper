namespace WebReaper.LinkTracker.Abstract
{
    public interface ILinkTracker
    {
        Task AddVisitedLink(string siteUrl, string visitedLink);
        Task<IEnumerable<string>> GetVisitedLinks(string siteName);
    }
}