using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WebReaper.Core.Markdown;
using WebReaper.Processing.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Processing.Concrete;

/// <summary>
/// The change-tracking page processor (ADR-0048). On every page:
/// extract Markdown, hash it, compare to the previously-stored hash
/// for the URL, decide <c>new</c> / <c>same</c> / <c>changed</c>,
/// annotate the record with <c>change_status</c>.
/// <para>
/// As of ADR-0063 the processor calls the
/// <see cref="HtmlToMarkdown.Convert"/> primitive directly — no LLM
/// dependency, no JsonObject wrapping/unwrapping. The Markdown
/// extraction strips template chrome so timestamp / ad-rotation /
/// session-ID noise does not flip the status spuriously.
/// </para>
/// </summary>
public sealed class ChangeTrackingProcessor : Abstract.IPageProcessor
{
    /// <summary>The annotation key the processor writes into the
    /// record. Values: <c>"new"</c>, <c>"same"</c>, <c>"changed"</c>.</summary>
    public const string StatusKey = "change_status";

    /// <summary>The annotation key for the previous hash (present when
    /// <see cref="StatusKey"/> is <c>"same"</c> or <c>"changed"</c>).</summary>
    public const string PreviousHashKey = "previous_hash";

    private readonly IChangeStore _store;

    /// <summary>Construct with a backing store (in-memory default;
    /// satellites are pluggable).</summary>
    public ChangeTrackingProcessor(IChangeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc/>
    public async ValueTask<PageVerdict> ProcessAsync(PageContext context, CancellationToken cancellationToken)
    {
        // ADR-0063: hash the Markdown directly via the public primitive;
        // no JsonObject construct-and-discard. Empty html still hashes
        // (the primitive returns an empty string, never throws).
        var markdown = HtmlToMarkdown.Convert(context.Html);
        var hash = ComputeHash(markdown);

        var url = context.Data.Url;
        var prior = await _store.TryReadAsync(url, cancellationToken);

        string status;
        if (prior is null)
            status = "new";
        else if (prior == hash)
            status = "same";
        else
            status = "changed";

        await _store.WriteAsync(url, hash, cancellationToken);

        // Annotate the record. The Data JsonObject is the record sinks
        // will receive — we mutate in place (the Crawl driver hands a
        // per-sink clone, so this mutation is safe).
        context.Data.Data[StatusKey] = JsonValue.Create(status);
        if (prior is not null)
            context.Data.Data[PreviousHashKey] = JsonValue.Create(prior);

        return PageVerdict.Keep(context.Data);
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
