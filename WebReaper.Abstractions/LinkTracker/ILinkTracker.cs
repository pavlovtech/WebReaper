namespace WebReaper.LinkTracker.Abstract
{
    public interface ILinkTracker
    {
        void AddVisitedLink(string siteUrl, string visitedLink);
        IEnumerable<string> GetVisitedLinks(string siteName);
    }
}