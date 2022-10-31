namespace WebReaper.LinkTracker.Abstract
{
    public interface IVisitedLinkTracker
    {
        Task AddVisitedLinkAsync(string siteId, string visitedLink);
        Task<IEnumerable<string>> GetVisitedLinksAsync(string siteId);
        Task<IEnumerable<string>> GetNotVisitedLinks(string siteId, IEnumerable<string> links);
        Task<long> GetVisitedLinksCount(string siteId);
    }
}