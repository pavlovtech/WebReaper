namespace WebReaper.Exceptions;

public class PageCrawlLimitException : Exception
{
    public PageCrawlLimitException(string message)
        : base(message)
    {
    }

    public PageCrawlLimitException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public int PageCrawlLimit { get; init; }
}