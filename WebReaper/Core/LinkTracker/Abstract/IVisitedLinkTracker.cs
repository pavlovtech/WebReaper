namespace WebReaper.Core.LinkTracker.Abstract;

public interface IVisitedLinkTracker
{
    public bool DataCleanupOnStart { get; set; }
    
    Task AddVisitedLinkAsync(string visitedLink);
    Task<List<string>> GetVisitedLinksAsync();
    Task<List<string>> GetNotVisitedLinks(IEnumerable<string> links);
    Task<long> GetVisitedLinksCount();
    
    Task Initialization { get; }
}