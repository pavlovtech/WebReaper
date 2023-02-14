namespace ExoScraper.Exceptions;

public class PageCrawlLimitException : Exception
{
    public int PageCrawlLimit { get; init; }

    public PageCrawlLimitException(string message)
        : base(message)
    {
    }

    public PageCrawlLimitException(string message, Exception inner)
        : base(message, inner)
    {
    }
}