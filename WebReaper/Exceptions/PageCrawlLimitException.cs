namespace WebReaper.Exceptions;

public class PageCrawlLimitException : Exception
{
    public int PageCrawlLimit { get; set; }

    public PageCrawlLimitException()
    {
    }

    public PageCrawlLimitException(string message)
        : base(message)
    {
    }

    public PageCrawlLimitException(string message, Exception inner)
        : base(message, inner)
    {
    }
}