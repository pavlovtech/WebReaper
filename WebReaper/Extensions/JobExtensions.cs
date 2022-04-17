using WebReaper.Domain;

public static class JobExtensions
{
    public static PageType GetPageType(this Job job) {
        if(job.LinkPathSelector == null)
            return PageType.TargetPage;
        if(job.LinkPathSelector.Next == null && job.PaginationSelector != null)
            return PageType.PageWithPagination;
        else
            return PageType.TransitPage;
    }
}