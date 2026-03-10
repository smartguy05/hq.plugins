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
            return await service.ProcessRequest(serviceRequest, config, NotificationService);
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
                var status = await sandbox.GetStatusAsync(workspaceId);
                _provisionedWorkspaces.Add(workspaceId);
            }
            catch
            {
                // Workspace doesn't exist yet — create it
                await sandbox.CreateWorkspaceAsync(workspaceId, null);
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

        await sandbox.WriteFileAsync("default", path, bytes);

        if (Logger != null)
            await Logger(LogLevel.Trace, $"[FileStorageProvider] wrote {bytes.Length} bytes to {path}");

        return path;
    }

    public async Task<string> ReadFileAsync(string path)
    {
        var sandbox = await GetSandboxAsync();
        try
        {
            var (_, content) = await sandbox.ReadFileAsync("default", path);
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
            await sandbox.ReadFileAsync("default", path);
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
        await sandbox.DeleteFileAsync("default", path, false);
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(string directory = "/workspace")
    {
        var sandbox = await GetSandboxAsync();
        var listing = await sandbox.ListFilesAsync("default", directory);
        // listing is the raw output from `ls` — parse into a list of names
        if (listing is string listStr)
            return listStr.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList().AsReadOnly();
        return Array.Empty<string>();
    }
}
