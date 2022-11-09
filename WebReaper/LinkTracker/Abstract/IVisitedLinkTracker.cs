namespace WebReaper.LinkTracker.Abstract;

public interface IVisitedLinkTracker
{
    Task AddVisitedLinkAsync(string siteId, string visitedLink);
    Task<List<string>> GetVisitedLinksAsync(string siteId);
    Task<List<string>> GetNotVisitedLinks(string siteId, IEnumerable<string> links);
    Task<long> GetVisitedLinksCount(string siteId);
}