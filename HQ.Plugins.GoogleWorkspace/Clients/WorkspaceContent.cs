namespace HQ.Plugins.GoogleWorkspace.Clients;

/// <summary>
/// SAFE-02 helper: wraps free-text / file content fetched from Drive, Docs and Sheets as
/// <see cref="HQ.Models.Safety.Untrusted{T}"/> so the host can classify it for prompt-injection
/// before it reaches an LLM. These files may be third-party-shared or externally editable.
/// </summary>
internal static class WorkspaceContent
{
    public static object AsUntrusted(string content, string provenance, string source) =>
        string.IsNullOrEmpty(content) ? content : new HQ.Models.Safety.Untrusted<string>(content, provenance, source);
}
