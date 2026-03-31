using HQ.Plugins.HeadlessBrowser.Pipeline;
using Microsoft.Playwright;

namespace HQ.Plugins.HeadlessBrowser;

public interface IBrowserClient : IAsyncDisposable
{
    bool IsInitialized { get; }
    PageSnapshot CurrentSnapshot { get; }
    PageSnapshot PreviousSnapshot { get; }
    Task<T> ExecuteAsync<T>(Func<IPage, Task<T>> action);
    Task ExecuteAsync(Func<IPage, Task> action);
    Task<PageSnapshot> TakeSnapshotAsync(int timeoutMs = 10000);
    ILocator ResolveRef(string refId);
}
