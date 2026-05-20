using WebReaper.Infra.Concrete;

namespace WebReaper.UnitTests;

// ADR-0026: the Crawl driver's retry around the per-Job Spider call is a
// named seam (IRetryPolicy) with FixedAttemptsRetryPolicy as the core
// default. Tests pin the contract: bounded attempts, cancellation is
// never retried, the action's last exception propagates on exhaustion.
public class RetryPolicyTests
{
    [Fact]
    public async Task First_attempt_success_invokes_action_once()
    {
        var calls = 0;
        var policy = new FixedAttemptsRetryPolicy();

        var result = await policy.ExecuteAsync(_ =>
        {
            calls++;
            return Task.FromResult(42);
        }, CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Succeeds_on_third_attempt_after_two_transient_failures()
    {
        var calls = 0;
        var policy = new FixedAttemptsRetryPolicy(maxAttempts: 4);

        var result = await policy.ExecuteAsync(_ =>
        {
            calls++;
            if (calls < 3) throw new InvalidOperationException("transient");
            return Task.FromResult("ok");
        }, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Last_exception_propagates_after_attempts_exhausted()
    {
        var calls = 0;
        var policy = new FixedAttemptsRetryPolicy(maxAttempts: 3);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<int>(_ =>
            {
                calls++;
                throw new InvalidOperationException($"attempt {calls}");
            }, CancellationToken.None));

        Assert.Equal("attempt 3", ex.Message);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task OperationCanceledException_thrown_by_action_propagates_immediately()
    {
        var calls = 0;
        var policy = new FixedAttemptsRetryPolicy(maxAttempts: 4);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync<int>(_ =>
            {
                calls++;
                throw new OperationCanceledException();
            }, CancellationToken.None));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TaskCanceledException_thrown_by_action_propagates_immediately()
    {
        // TaskCanceledException : OperationCanceledException — the catch
        // filter must cover the subclass.
        var calls = 0;
        var policy = new FixedAttemptsRetryPolicy(maxAttempts: 4);

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            policy.ExecuteAsync<int>(_ =>
            {
                calls++;
                throw new TaskCanceledException();
            }, CancellationToken.None));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Token_cancelled_before_first_attempt_throws_without_invoking_action()
    {
        var calls = 0;
        var policy = new FixedAttemptsRetryPolicy();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync(_ =>
            {
                calls++;
                return Task.FromResult(0);
            }, cts.Token));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Token_cancelled_between_attempts_short_circuits_remaining_attempts()
    {
        var calls = 0;
        using var cts = new CancellationTokenSource();
        var policy = new FixedAttemptsRetryPolicy(maxAttempts: 4);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync<int>(_ =>
            {
                calls++;
                cts.Cancel(); // trip the token from inside the first attempt
                throw new InvalidOperationException("transient");
            }, cts.Token));

        // After attempt 1 throws InvalidOperationException, the catch
        // filter swallows it (attempt < maxAttempts), then the loop's
        // head ThrowIfCancellationRequested sees the cancellation and
        // throws OCE. No second attempt.
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MaxAttempts_one_means_no_retry()
    {
        var calls = 0;
        var policy = new FixedAttemptsRetryPolicy(maxAttempts: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<int>(_ =>
            {
                calls++;
                throw new InvalidOperationException("first throw wins");
            }, CancellationToken.None));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Ctor_rejects_zero_or_negative_max_attempts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedAttemptsRetryPolicy(maxAttempts: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedAttemptsRetryPolicy(maxAttempts: -1));
    }

    [Fact]
    public async Task Null_action_throws_ArgumentNullException()
    {
        var policy = new FixedAttemptsRetryPolicy();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            policy.ExecuteAsync<int>(null!, CancellationToken.None));
    }
}
