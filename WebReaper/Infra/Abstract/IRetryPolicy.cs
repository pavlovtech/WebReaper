namespace WebReaper.Infra.Abstract;

/// <summary>
/// Bounds retries on the per-Job <see cref="Core.Spider.Abstract.ISpider"/>
/// call (ADR-0022) before the Crawl driver gives up on the Job (ADR-0026).
/// One <c>IRetryPolicy</c> lives on the in-process Crawl driver
/// (<see cref="Core.ScraperEngine"/>); the distributed-worker reduced shell
/// (ADR-0009) does not use one — its retry knob is the queue's redelivery /
/// visibility-timeout, not an in-process loop.
/// </summary>
/// <remarks>
/// <para>
/// The core default is the internal <c>FixedAttemptsRetryPolicy</c> — four
/// attempts total (one initial + three retries, the exact pre-0026
/// behaviour), no delay between attempts,
/// <see cref="OperationCanceledException"/> propagates immediately rather
/// than triggering a retry. Replace it via
/// <see cref="WebReaper.Builders.ScraperEngineBuilder.WithRetryPolicy"/>:
/// a no-retry adapter (deterministic tests), a backoff policy (a Polly
/// resilience pipeline wrapped in an <c>IRetryPolicy</c>), or a future
/// satellite-aware policy.
/// </para>
/// <para>
/// Implementations <b>must</b> propagate <see cref="OperationCanceledException"/>
/// (and its <see cref="System.Threading.Tasks.TaskCanceledException"/>
/// subclass) without retrying; cancellation is cooperative, not transient.
/// Implementations <b>must</b> rethrow the action's last non-cancellation
/// exception when retries are exhausted.
/// </para>
/// </remarks>
public interface IRetryPolicy
{
    /// <summary>
    /// Run <paramref name="action"/> with the policy's retry behaviour and
    /// return its value. On retry-worthy failure the policy invokes
    /// <paramref name="action"/> again (with the same
    /// <paramref name="cancellationToken"/>) until it succeeds or the
    /// policy's attempts are exhausted; on exhaustion the last exception
    /// propagates.
    /// </summary>
    /// <typeparam name="T">The action's return type.</typeparam>
    /// <param name="action">The unit of work to attempt. The policy
    /// passes <paramref name="cancellationToken"/> in so the action can
    /// cooperate with cancellation between awaits.</param>
    /// <param name="cancellationToken">Cooperatively cancels both the
    /// current attempt and any further attempts; an
    /// <see cref="OperationCanceledException"/> propagates immediately,
    /// regardless of remaining attempts.</param>
    /// <exception cref="OperationCanceledException">The token was
    /// cancelled, either before the first attempt or during an attempt.
    /// Never retried.</exception>
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}
