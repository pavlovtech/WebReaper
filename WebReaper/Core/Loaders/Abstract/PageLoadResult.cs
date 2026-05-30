namespace WebReaper.Core.Loaders.Abstract;

/// <summary>
/// What a <see cref="IPageLoader"/> / <see cref="IPageLoadTransport"/> returns
/// for one page (ADR-0083): the page body plus the response metadata a consumer
/// (or, in later slices, the block detector) needs. Replaces the bare HTML
/// string the loader returned before, so the library can report an HTTP status
/// and response headers for the first time.
/// <para>
/// Init-only properties, deliberately not a positional record: this is a
/// growing bag of response metadata consumed by field access, where
/// deconstruction buys nothing and a positional arity change would break every
/// transport on each new field. Future fields (FinalUrl, ContentType) land
/// additively.
/// </para>
/// </summary>
public sealed record PageLoadResult
{
    /// <summary>The page body as loaded: the HTTP response body for the HTTP
    /// transport, or the rendered DOM for a browser transport.</summary>
    public required string Html { get; init; }

    /// <summary>The HTTP status of the main-document response, or <c>null</c>
    /// when the transport cannot determine it (for example the CDP transport,
    /// which does not yet surface the navigation status).</summary>
    public int? HttpStatus { get; init; }

    /// <summary>The main-document response headers, compared case-insensitively
    /// by key. Empty when the transport does not surface them.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = EmptyHeaders;

    /// <summary>A shared empty, case-insensitive header map, used as the default
    /// so the common no-headers case allocates nothing.</summary>
    public static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
