namespace WebReaper.Exceptions;

/// <summary>
/// Thrown by the config payload shell when the scraper config is absent.
/// The uniform missing-value policy (ADR 0003): absence of a config is
/// unrecoverable — <c>CreateConfigAsync</c> must run before
/// <c>GetConfigAsync</c>. Replaces the file adapter's bare
/// <see cref="NullReferenceException"/> and the other backends' silent
/// <c>null</c>.
/// </summary>
public class ConfigNotFoundException : Exception
{
    /// <summary>The config was absent for storage key
    /// <paramref name="key"/>.</summary>
    public ConfigNotFoundException(string key)
        : base($"No scraper config found for key '{key}'. CreateConfigAsync must run before GetConfigAsync.")
    {
        Key = key;
    }

    /// <summary>The storage key that had no persisted config.</summary>
    public string Key { get; init; }
}
