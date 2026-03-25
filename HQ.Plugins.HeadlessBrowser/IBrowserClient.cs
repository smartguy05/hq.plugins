using Microsoft.Playwright;

namespace HQ.Plugins.HeadlessBrowser;

public interface IBrowserClient : IAsyncDisposable
{
    bool IsInitialized { get; }
    Task<T> ExecuteAsync<T>(Func<IPage, Task<T>> action);
    Task ExecuteAsync(Func<IPage, Task> action);
}
