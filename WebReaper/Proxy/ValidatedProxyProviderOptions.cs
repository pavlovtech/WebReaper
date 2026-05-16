namespace WebReaper.Proxy;

/// <summary>
/// Tuning knobs for <see cref="Concrete.ValidatedProxyProvider"/>.
/// </summary>
public sealed class ValidatedProxyProviderOptions
{
    /// <summary>
    /// How long a validated list is reused before the source is queried
    /// and proxies are re-validated. Default: 5 minutes.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Per-proxy timeout for a single validator. A validator that exceeds
    /// this is treated as a failed (invalid) proxy. Default: 10 seconds.
    /// </summary>
    public TimeSpan ValidationTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum number of proxies validated concurrently. Default: 20.
    /// </summary>
    public int MaxConcurrentValidations { get; set; } = 20;
}
