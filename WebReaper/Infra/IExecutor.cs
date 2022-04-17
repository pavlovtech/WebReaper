using System.Runtime.CompilerServices;

namespace WebReaper.Infra
{
    public interface IExecutor
    {
        Task<T> Run<T>(Func<Task<T>> func, [CallerMemberName] string callerName = "");
        void Run(Action action, [CallerMemberName] string callerName = "");
        Task<T> RunWithRetry<T>(Func<Task<T>> func, [CallerMemberName] string callerName = "");
        void RunWithRetry<T>(Action action, [CallerMemberName] string callerName = "");
    }
}