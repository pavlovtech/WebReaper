namespace WebReaper.Domain
{
    public record Job(
    string BaseUrl,
    string Url,
    LinkedListNode<string>? LinkPathSelector,
    string? PaginationSelector,
    int Priority = 0);
}