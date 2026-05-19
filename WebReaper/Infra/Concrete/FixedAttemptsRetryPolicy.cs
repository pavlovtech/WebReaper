using WebReaper.Infra.Abstract;

namespace WebReaper.Infra.Concrete;

/// <summary>
/// The core default <see cref="IRetryPolicy"/> (ADR-0026): a fixed maximum
/// number of attempts, no delay between them, every exception except
/// <see cref="OperationCanceledException"/> triggers a retry. The default
/// <c>maxAttempts = 4</c> (one initial + three retries) reproduces the
/// pre-0026 <c>Polly.Policy.Handle&lt;Exception&gt;().RetryAsync(3)</c>
/// behaviour exactly — minus the latent bug where cancellation was caught
/// and retried.
/// </summary>
internal sealed class FixedAttemptsRetryPolicy : IRetryPolicy
{
    private readonly int _maxAttempts;

    /// <summary>
    /// </summary>
    /// <param name="maxAttempts">Total attempts including the initial one.
    /// Must be at least 1. Default 4 — matches the pre-0026 Polly
    /// behaviour.</param>
    public FixedAttemptsRetryPolicy(int maxAttempts = 4)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts), maxAttempts,
                "maxAttempts must be at least 1 (one initial attempt).");
        }

        _maxAttempts = maxAttempts;
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        for (var attempt = 1; ; attempt++)
        {
            // Cooperative cancellation check at the head of every attempt:
            // a token tripped between attempts must not start a fresh one.
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ADR-0026: cancellation is cooperative, not transient.
                // Propagate without retrying. Covers TaskCanceledException
                // by subclass.
                throw;
            }
            catch when (attempt < _maxAttempts)
            {
                // Retry. No delay — backoff is a custom-policy concern,
                // not the core default's responsibility.
            }
        }
    }
}
