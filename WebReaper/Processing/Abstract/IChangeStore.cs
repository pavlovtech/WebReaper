namespace WebReaper.Processing.Abstract;

/// <summary>
/// The change-tracking store seam (ADR-0048). One value per URL — the
/// hash of the previous extracted Markdown. The default in-memory
/// adapter ships in core; persistent adapters (Redis / File) land as
/// satellite ADRs.
/// <para>
/// Separate from <see cref="WebReaper.Core.Loaders.Abstract.IPageCache"/>
/// (ADR-0041) — different lifecycle (no TTL), different key (URL only;
/// page cache is keyed by (URL, PageType)), different value (hash, not
/// full HTML).
/// </para>
/// </summary>
public interface IChangeStore
{
    /// <summary>Return the previously-stored hash for
    /// <paramref name="url"/>, or <c>null</c> if unseen.</summary>
    Task<string?> TryReadAsync(string url, CancellationToken cancellationToken);

    /// <summary>Persist <paramref name="hash"/> as the latest snapshot
    /// for <paramref name="url"/>.</summary>
    Task WriteAsync(string url, string hash, CancellationToken cancellationToken);
}
