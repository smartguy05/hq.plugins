namespace HQ.Plugins.Microsoft365.Graph;

/// <summary>
/// SAFE-02 helper: wraps document/file content fetched from OneDrive/SharePoint, Excel and Word
/// as <see cref="HQ.Models.Safety.Untrusted{T}"/> so the host can classify it for prompt-injection
/// before it reaches an LLM. These files may be third-party-shared or externally editable.
/// </summary>
internal static class M365Content
{
    public static object AsUntrusted(string content, string provenance, string source) =>
        string.IsNullOrEmpty(content) ? content : new HQ.Models.Safety.Untrusted<string>(content, provenance, source);
}
