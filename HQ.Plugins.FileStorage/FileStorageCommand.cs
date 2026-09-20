using System.Text;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.FileStorage.Models;

namespace HQ.Plugins.FileStorage;

public class FileStorageCommand : CommandBase<ServiceRequest, ServiceConfig>, IFileStorageProvider
{
    public override string Name => "File Storage";
    public override string Description => "Docker-based sandboxed file workspaces with Python and Node.js";
    protected override INotificationService NotificationService { get; set; }

    private ServiceConfig _config;
    private readonly HashSet<string> _provisionedWorkspaces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// WP6A-5 side effect: this class also implements <see cref="IFileStorageProvider"/>, a
    /// plugin-to-plugin collaboration surface (e.g. ReportGeneratorCommand writing report files)
    /// wired up via <c>ICommand.SetFileStorageProvider</c>. That interface — defined in
    /// HQ.Models, not owned by this cluster — carries no tenant/org id at all, so there is no
    /// caller-scoped OrganizationId available here the way there is on the workspace_* tool
    /// calls below (those get it from the host-injected ServiceRequest JSON). Previously this
    /// path used a single hardcoded "default" workspace name with NO scoping component
    /// whatsoever, meaning every agent/org on the host that used a plugin backed by
    /// IFileStorageProvider shared the exact same literal Docker container. Since plugin
    /// instances are cached per-agent (see PluginService._agentPluginCache), scoping the
    /// "default" workspace to a random id generated once per instance at least confines this
    /// path to the owning agent instead of leaking across every tenant on the host. This is NOT
    /// a full org-scoping fix (there is no tenant identity to scope BY here) — closing it
    /// properly requires IFileStorageProvider to carry a caller org id, which is a HQ.Models
    /// interface change outside this cluster's owned files.
    /// </summary>
    private readonly Guid _instanceScopeId = Guid.NewGuid();

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<FileStorageService>();
    }

    public override Task<object> Initialize(string config, LogDelegate logFunction, INotificationService notificationService)
    {
        _config = config.ReadPluginConfig<ServiceConfig>();
        return base.Initialize(config, logFunction, notificationService);
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            var service = new FileStorageService(config, Logger);
            return await service.ProcessRequest(RawServiceRequest, config, NotificationService);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", e);
            return new
            {
                Success = false,
                Message = $"Error: {e.Message}"
            };
        }
    }

    // ───────────────────────────── IFileStorageProvider ─────────────────────────────

    private async Task<DockerSandbox> GetSandboxAsync(string workspaceId = "default")
    {
        var config = _config ?? throw new InvalidOperationException(
            "FileStorage plugin not initialized. Ensure the plugin is configured and initialized before using file storage.");

        var sandbox = new DockerSandbox(config);

        // Auto-provision workspace if needed
        if (!_provisionedWorkspaces.Contains(workspaceId))
        {
            try
            {
                var status = await sandbox.GetStatusAsync(_instanceScopeId, workspaceId);
                _provisionedWorkspaces.Add(workspaceId);
            }
            catch
            {
                // Workspace doesn't exist yet — create it
                await sandbox.CreateWorkspaceAsync(_instanceScopeId, workspaceId, null);
                _provisionedWorkspaces.Add(workspaceId);
            }
        }

        return sandbox;
    }

    public async Task<string> WriteFileAsync(string path, string content, bool isBase64 = false)
    {
        var sandbox = await GetSandboxAsync();
        var bytes = isBase64
            ? Convert.FromBase64String(content)
            : Encoding.UTF8.GetBytes(content);

        await sandbox.WriteFileAsync(_instanceScopeId, "default", path, bytes);

        if (Logger != null)
            await Logger(LogLevel.Trace, $"[FileStorageProvider] wrote {bytes.Length} bytes to {path}");

        return path;
    }

    public async Task<string> ReadFileAsync(string path)
    {
        var sandbox = await GetSandboxAsync();
        try
        {
            var (_, content) = await sandbox.ReadFileAsync(_instanceScopeId, "default", path);
            return Encoding.UTF8.GetString(content);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> FileExistsAsync(string path)
    {
        var sandbox = await GetSandboxAsync();
        try
        {
            await sandbox.ReadFileAsync(_instanceScopeId, "default", path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task DeleteFileAsync(string path)
    {
        var sandbox = await GetSandboxAsync();
        await sandbox.DeleteFileAsync(_instanceScopeId, "default", path, false);
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(string directory = "/workspace")
    {
        var sandbox = await GetSandboxAsync();
        var listing = await sandbox.ListFilesAsync(_instanceScopeId, "default", directory);
        // listing is the raw output from `ls` — parse into a list of names
        if (listing is string listStr)
            return listStr.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList().AsReadOnly();
        return Array.Empty<string>();
    }
}
