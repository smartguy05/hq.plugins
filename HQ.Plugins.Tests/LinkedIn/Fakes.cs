using HQ.Models.Interfaces;
using HQ.Plugins.LinkedIn;

namespace HQ.Plugins.Tests.LinkedIn;

/// <summary>In-memory <see cref="ILinkedInBrowser"/> for testing the service without a live LinkedIn session.</summary>
internal sealed class FakeLinkedInBrowser : ILinkedInBrowser
{
    public bool Authenticated = true;
    public Func<string, string, object, VoyagerResponse> OnVoyager;
    public readonly List<(string Method, string Path, object Body)> Calls = new();

    public Task<bool> IsAuthenticatedAsync() => Task.FromResult(Authenticated);

    public Task<VoyagerResponse> VoyagerAsync(string method, string path, object body = null)
    {
        Calls.Add((method, path, body));
        var response = OnVoyager?.Invoke(method, path, body) ?? new VoyagerResponse(200, "{\"plainId\":42}");
        return Task.FromResult(response);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Notification service fake that records confirmation requests and lets tests mark ids as existing.</summary>
internal sealed class FakeNotificationService : INotificationService
{
    public readonly HashSet<Guid> Existing = new();
    public int RequestCount;

    public Task<object> Confirm(Guid confirmationId, bool confirm) => Task.FromResult<object>(null);

    public bool DoesConfirmationExist(Guid confirmationId, out HQ.Models.Confirmation confirmation)
    {
        confirmation = null;
        return Existing.Contains(confirmationId);
    }

    public Task<object> RequestConfirmation(string serviceName, HQ.Models.Confirmation confirmation, IPluginServiceRequest serviceRequest)
    {
        RequestCount++;
        return Task.FromResult<object>(new { ConfirmationRequested = true, confirmation.Id });
    }

    public Task<object> SendNotification(string message) => Task.FromResult<object>(null);
}
