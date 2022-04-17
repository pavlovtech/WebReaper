using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace WebReaper.Infra
{
    public class Executor : IExecutor
    {
        protected AsyncRetryPolicy asyncPolicy;
        protected RetryPolicy policy;

        private ILogger<Executor> _logger;

        public Executor(ILogger<Executor> logger)
        {
            _logger = logger;
            asyncPolicy = Policy.Handle<Exception>().RetryAsync(3);
            policy = Policy.Handle<Exception>().Retry(3);
        }

        public async Task<T> Run<T>(Func<Task<T>> func, [CallerMemberName] string callerName = "")
        {
            _logger.LogInformation("Started executing {method}.", callerName);

            var watch = Stopwatch.StartNew();

            var result = await func();

            watch.Stop();

            _logger.LogInformation("Finished executing {method}. Elapsed time: {elapsed} ms.",
                callerName,
                watch.ElapsedMilliseconds);

            return result;
        }

        public void Run(Action action, [CallerMemberName] string callerName = "")
        {
            var watch = Stopwatch.StartNew();

            action();

            watch.Stop();

            _logger.LogInformation("Finished executing {method}. Elapsed time: {elapsed} ms",
                callerName,
                watch.ElapsedMilliseconds);
        }

        public async Task<T> RunWithRetry<T>(Func<Task<T>> func, [CallerMemberName] string callerName = "")
        {
            return await asyncPolicy.ExecuteAsync(async () => await Run(func, callerName));
        }

        public void RunWithRetry<T>(Action action, [CallerMemberName] string callerName = "")
        {
            policy.Execute(() => Run(action, callerName));
        }
    }
}