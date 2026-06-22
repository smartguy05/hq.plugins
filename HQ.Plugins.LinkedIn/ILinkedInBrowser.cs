using System.Text.Json;

namespace HQ.Plugins.LinkedIn;

/// <summary>Result of an authenticated LinkedIn Voyager request issued from inside the logged-in page.</summary>
public sealed record VoyagerResponse(int Status, string RawBody)
{
    public bool IsSuccess => Status is >= 200 and < 300;

    /// <summary>Parsed JSON body, or a JSON null element when the body isn't valid JSON.</summary>
    public JsonElement Json
    {
        get
        {
            try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(RawBody) ? "null" : RawBody).RootElement.Clone(); }
            catch (JsonException) { return JsonDocument.Parse("null").RootElement.Clone(); }
        }
    }
}

/// <summary>
/// Abstraction over a single authenticated LinkedIn browser session. The service depends
/// on this (not on Playwright directly) so tools can be unit-tested with a fake. The real
/// implementation drives a persistent Chromium profile; the agent never sees cookies or
/// the password — it only issues the semantic calls below.
/// </summary>
public interface ILinkedInBrowser : IAsyncDisposable
{
    /// <summary>True when the persisted session is still logged in (a cheap Voyager probe).</summary>
    Task<bool> IsAuthenticatedAsync();

    /// <summary>
    /// Issues an authenticated request to LinkedIn's internal Voyager API from within the
    /// logged-in page, so it carries the real session cookies (incl. httpOnly <c>li_at</c>),
    /// CSRF token and origin. <paramref name="path"/> is relative to the LinkedIn origin
    /// (e.g. <c>/voyager/api/identity/dash/profiles?...</c>).
    /// </summary>
    Task<VoyagerResponse> VoyagerAsync(string method, string path, object body = null);
}
