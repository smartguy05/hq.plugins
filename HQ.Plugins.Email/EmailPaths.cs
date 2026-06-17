namespace HQ.Plugins.Email;

/// <summary>
/// Shared resolution of the per-agent SQLite cache location. Both the plugin's
/// background init (EmailCommand) and the inbox-viewer HTTP routes (EmailEndpoints)
/// must agree on these paths so the routes can open the same DB an agent syncs into.
/// </summary>
public static class EmailPaths
{
    /// <summary>
    /// Directory holding the per-agent email cache DBs. Defaults to a folder next to
    /// the plugin DLL, but honors the <c>HQ_EMAIL_DATA_DIR</c> environment variable so
    /// deployments can point it at a writable, persistent volume (the plugin directory
    /// is mounted read-only in production).
    /// </summary>
    public static string EmailDataDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("HQ_EMAIL_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return overrideDir;

        var pluginDir = Path.GetDirectoryName(typeof(EmailPaths).Assembly.Location)!;
        return Path.Combine(pluginDir, "EmailData");
    }

    /// <summary>SQLite file name for an agent (or the shared file when no agent id).</summary>
    public static string DbFileName(string agentId) =>
        !string.IsNullOrWhiteSpace(agentId) ? $"agent-{agentId}-emails.db" : "emails.db";

    /// <summary>Full path to an agent's email cache DB file.</summary>
    public static string DbPath(string agentId) =>
        Path.Combine(EmailDataDir(), DbFileName(agentId));

    /// <summary>SQLite connection string for an agent's email cache.</summary>
    public static string ResolveConnectionString(string agentId) =>
        $"Data Source={DbPath(agentId)}";

    /// <summary>
    /// The agent id encoded in a cache file name, or null for the shared file.
    /// Inverse of <see cref="DbFileName"/>.
    /// </summary>
    public static string AgentIdFromFileName(string fileName)
    {
        const string prefix = "agent-";
        const string suffix = "-emails.db";
        if (string.IsNullOrEmpty(fileName) ||
            fileName.Length <= prefix.Length + suffix.Length ||
            !fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(suffix, StringComparison.Ordinal))
            return null;
        return fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
    }
}
