using Polly;
using Polly.Retry;

namespace Exoscan.Infra;

public static class Executor
{
    public static AsyncRetryPolicy AsyncPolicy { get; set; } = Polly.Policy.Handle<Exception>().RetryAsync(3);
    public static RetryPolicy Policy { get; set; } = Polly.Policy.Handle<Exception>().Retry(3);

    public static async Task<T> RetryAsync<T>(Func<Task<T>> func) =>
        await AsyncPolicy.ExecuteAsync(async() => await func());

    public static void Retry<T>(Action action) => Policy.Execute(() => action());
}