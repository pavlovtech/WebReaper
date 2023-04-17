using Polly;
using Polly.Retry;

namespace WebReaper.Infra;

public static class Executor
{
    private static AsyncRetryPolicy AsyncPolicy { get; } = Polly.Policy.Handle<Exception>().RetryAsync(3);
    private static RetryPolicy Policy { get; } = Polly.Policy.Handle<Exception>().Retry(3);

    public static async Task<T> RetryAsync<T>(Func<Task<T>> func) => await AsyncPolicy.ExecuteAsync(func);

    public static void Retry<T>(Action action) => Policy.Execute(action);
}