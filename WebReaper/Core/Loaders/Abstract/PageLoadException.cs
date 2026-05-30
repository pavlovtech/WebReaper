namespace WebReaper.Core.Loaders.Abstract;

/// <summary>
/// Thrown by a <see cref="IPageLoadTransport"/> when there is no HTTP response
/// at all (ADR-0083): DNS failure, connection refused, TLS error, or timeout.
/// A completed response with any status code, including 4xx and 5xx, is
/// returned as a <see cref="PageLoadResult"/> rather than thrown — a non-2xx is
/// data, not a fault. This narrows ADR-0004's "a page that cannot be retrieved
/// is an exception" stance to the genuine no-response case.
/// </summary>
public sealed class PageLoadException : Exception
{
    /// <summary>Create a load failure with a message and an optional underlying
    /// transport exception (the <see cref="System.Net.Http.HttpRequestException"/>
    /// or timeout that caused it).</summary>
    public PageLoadException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
