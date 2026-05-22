namespace WebReaper.Infra.Abstract;

/// <summary>
/// The opt-in <em>adapter warm-up</em> capability (ADR-0033): an adapter that
/// must do async work — a connection, a cursor restore, a
/// <c>DataCleanupOnStart</c> wipe — once, before it is first used, declares
/// it by implementing this interface. The init-side mirror of
/// <see cref="System.IAsyncDisposable"/>: orthogonal to the role interfaces
/// (<c>IScheduler</c>, <c>IVisitedLinkTracker</c>, <c>IScraperSink</c>), and
/// implemented <b>only</b> by the adapters that actually warm up — an adapter
/// with no async warm-up (the in-memory scheduler and tracker, the console
/// and file sinks) implements nothing.
/// </summary>
/// <remarks>
/// <para>
/// Warm-up is <b>owner-driven</b>. The in-process Crawl driver
/// (<see cref="WebReaper.Core.ScraperEngine"/>) calls
/// <see cref="InitializeAsync"/> on every adapter it holds that implements
/// this interface — the scheduler, the visited-link tracker, every sink —
/// once, before the crawl loop. The consumer-authored distributed driver
/// (ADR-0009) drives warm-up itself.
/// </para>
/// <para>
/// Constructing an adapter performs no async work (ADR-0033 retires the
/// constructor-fired <c>Initialization = InitializeAsync()</c> shape);
/// <see cref="InitializeAsync"/> is the only path to it. An adapter must be
/// warmed up before any other member is called.
/// </para>
/// </remarks>
public interface IAsyncInitializable
{
    /// <summary>
    /// Perform the adapter's one-time async warm-up. <b>Idempotent</b>: the
    /// first call performs the warm-up; every later call returns the same
    /// completed task without repeating it — so a per-message distributed
    /// driver may call it freely, and a destructive <c>DataCleanupOnStart</c>
    /// wipe runs at most once.
    /// </summary>
    Task InitializeAsync();
}
