using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.FileStorage.Models;

namespace HQ.Plugins.FileStorage;

public partial class FileStorageService
{
    private readonly DockerSandbox _sandbox;
    private readonly LogDelegate _logger;

    private static readonly HashSet<string> ProtectedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/", "/workspace", "/shared", "/home", "/home/agent", "/tmp", "/run", "/etc", "/usr", "/bin", "/sbin", "/var"
    };

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9\-]*$")]
    private static partial Regex WorkspaceIdPattern();

    public FileStorageService(ServiceConfig config, LogDelegate logger)
    {
        _logger = logger;
        _sandbox = new DockerSandbox(config);
    }

    private static void ValidateWorkspaceId(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Missing required parameter: workspaceId");
        if (!WorkspaceIdPattern().IsMatch(workspaceId))
            throw new ArgumentException("workspaceId must contain only alphanumeric characters and hyphens, and must start with an alphanumeric character");
    }

    /// <summary>
    /// WP6A-5 (High, 2026-09 security review): every workspace/team-scoped tool call must carry
    /// a caller-scoped organization id. HQ.Services.Plugin.PluginService.InjectOrganizationId
    /// force-overwrites <c>OrganizationId</c> on the incoming ServiceRequest JSON with the
    /// calling agent's authoritative OrganizationId before this plugin ever sees the call — the
    /// model cannot forge a different tenant's id here. A null/empty value means the call did not
    /// come through that host path (or org is genuinely absent), so we FAIL CLOSED rather than
    /// falling back to a global/unscoped namespace.
    /// </summary>
    private static Guid RequireOrganizationId(Guid? organizationId)
    {
        if (organizationId is null || organizationId == Guid.Empty)
            throw new UnauthorizedAccessException(
                "Missing organization context; refusing to operate on any workspace without a caller-scoped organization id.");
        return organizationId.Value;
    }

    /// <summary>
    /// WP6A-5 (partial hardening): teamId was previously taken verbatim and used to build the
    /// shared volume name (hq-team-{teamId}) with no format check at all — unlike workspaceId.
    /// This does not by itself scope teamId to a caller's org (the plugin has no tenant context
    /// available to it at all; see the WP6A-5 deferral note), but it does close the separate gap
    /// of an unvalidated string being used to name a Docker volume.
    /// </summary>
    private static void ValidateTeamId(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return;
        if (!WorkspaceIdPattern().IsMatch(teamId))
            throw new ArgumentException("teamId must contain only alphanumeric characters and hyphens, and must start with an alphanumeric character");
    }

    /// <summary>
    /// WP6A-11 (this cluster's scope): exec commands are attacker/agent-controlled text that was
    /// previously logged verbatim into the shared log sink. Truncate what reaches the log so a
    /// large or crafted payload cannot flood or pollute it, while keeping enough of a preview for
    /// debugging.
    /// </summary>
    private static string TruncateForLog(string text, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Length <= maxLength
            ? text
            : text[..maxLength] + $"...(truncated, {text.Length} chars total)";
    }

    // ───────────────────────────── Workspace Lifecycle ─────────────────────────────

    [Display(Name = "workspace_create")]
    [Description("Create a new persistent Docker workspace with Python 3, Node.js, and common CLI tools pre-installed. No network access. Files persist across restarts via Docker volumes. Optionally specify a teamId to mount a shared volume at /shared for cross-workspace collaboration.")]
    [Parameters(typeof(CreateWorkspaceArgs))]
    public async Task<object> CreateWorkspace(ServiceConfig config, CreateWorkspaceArgs request)
    {
        var orgId = RequireOrganizationId(request.OrganizationId);
        ValidateWorkspaceId(request.WorkspaceId);
        ValidateTeamId(request.TeamId);

        await _logger(LogLevel.Info, $"[FileAccess] org={orgId} workspace={request.WorkspaceId} action=create teamId={request.TeamId ?? "none"}");

        return await _sandbox.CreateWorkspaceAsync(orgId, request.WorkspaceId, request.TeamId);
    }

    [Display(Name = "workspace_destroy")]
    [Description("Stop and remove a workspace container and its data volume. Team shared volumes are preserved. This action is irreversible.")]
    [Parameters(typeof(DestroyWorkspaceArgs))]
    public async Task<object> DestroyWorkspace(ServiceConfig config, DestroyWorkspaceArgs request)
    {
        var orgId = RequireOrganizationId(request.OrganizationId);
        ValidateWorkspaceId(request.WorkspaceId);

        await _logger(LogLevel.Info, $"[FileAccess] org={orgId} workspace={request.WorkspaceId} action=destroy");

        return await _sandbox.DestroyWorkspaceAsync(orgId, request.WorkspaceId);
    }

    [Display(Name = "workspace_list")]
    [Description("List all of your organization's HQ workspaces with their IDs, team associations, and current status.")]
    [Parameters(typeof(ListWorkspacesArgs))]
    public async Task<object> ListWorkspaces(ServiceConfig config, ListWorkspacesArgs request)
    {
        var orgId = RequireOrganizationId(request.OrganizationId);

        await _logger(LogLevel.Info, $"[FileAccess] org={orgId} action=list_workspaces");

        return await _sandbox.ListWorkspacesAsync(orgId);
    }

    [Display(Name = "workspace_status")]
    [Description("Get detailed status of a workspace including container state, mounts, and resource configuration.")]
    [Parameters(typeof(WorkspaceStatusArgs))]
    public async Task<object> GetWorkspaceStatus(ServiceConfig config, WorkspaceStatusArgs request)
    {
        var orgId = RequireOrganizationId(request.OrganizationId);
        ValidateWorkspaceId(request.WorkspaceId);

        await _logger(LogLevel.Info, $"[FileAccess] org={orgId} workspace={request.WorkspaceId} action=status");

        return await _sandbox.GetStatusAsync(orgId, request.WorkspaceId);
    }

    // ───────────────────────────── File Operations ─────────────────────────────

    [Display(Name = "workspace_write_file")]
    [Description("Write a file to a workspace. Content can be plain text or base64-encoded binary. Parent directories are created automatically. Files are written to the persistent /workspace volume.")]
    [Parameters(typeof(WriteFileArgs))]
    public async Task<object> WriteFile(ServiceConfig config, WriteFileArgs request)
    {
        var orgId = RequireOrganizationId(request.OrganizationId);
        ValidateWorkspaceId(request.WorkspaceId);
        if (string.IsNullOrWhiteSpace(request.FilePath))
            throw new ArgumentException("Missing required parameter: filePath");
        if (request.FileContent == null)
            throw new ArgumentException("Missing required parameter: fileContent");

        var content = request.IsBase64 == true
            ? Convert.FromBase64String(request.FileContent)
            : Encoding.UTF8.GetBytes(request.FileContent);

        await _logger(LogLevel.Info, $"[FileAccess] org={orgId} workspace={request.WorkspaceId} action=write path={request.FilePath} bytes={content.Length}");

        await _sandbox.WriteFileAsync(orgId, request.WorkspaceId, request.FilePath, content);

        return new
        {
            Success = true,
            WorkspaceId = request.WorkspaceId,
            Path = request.FilePath,
            BytesWritten = content.Length
        };
    }

    [Display(Name = "workspace_read_file")]
    [Description("Read a file from a workspace. Returns the content as base64-encoded data along with the file size.")]
    [Parameters(typeof(ReadFileArgs))]
    public async Task<object> ReadFile(ServiceConfig config, ReadFileArgs request)
    {
        var orgId = RequireOrganizationId(request.OrganizationId);
        ValidateWorkspaceId(request.WorkspaceId);
        if (string.IsNullOrWhiteSpace(request.FilePath))
            throw new ArgumentException("Missing required parameter: filePath");

        await _logger(LogLevel.Info, $"[FileAccess] org={orgId} workspace={request.WorkspaceId} action=read path={request.FilePath}");

        var (fileName, content) = await _sandbox.ReadFileAsync(orgId, request.WorkspaceId, request.FilePath);

        return new
        {
            Success = true,
            WorkspaceId = request.WorkspaceId,
            Path = request.FilePath,
            FileName = fileName,
            Content = Convert.ToBase64String(content),
            SizeBytes = content.Length
        };
    }

    [Display(Name = "workspace_list_files")]
    [Description("List files and directories at a path in the workspace. Defaults to /workspace if no path specified.")]
    [Parameters(typeof(ListFilesArgs))]
    public async Task<object> ListFiles(ServiceConfig config, ListFilesArgs request)
    {
        var orgId = RequireOrganizationId(request.OrganizationId);
        ValidateWorkspaceId(request.WorkspaceId);

        var path = string.IsNullOrWhiteSpace(request.FilePath) ? "/workspace" : request.FilePath;

        await _logger(LogLevel.Info, $"[FileAccess] org={orgId} workspace={request.WorkspaceId} action=list path={path}");

        var listing = await _sandbox.ListFilesAsync(orgId, request.WorkspaceId, path);

        return new
        {
            Success = true,
            WorkspaceId = request.WorkspaceId,
            Path = path,
            Listing = listing
        };
    }

    [Display(Name = "workspace_delete_file")]
    [Description("Delete a file or directory from a workspace. Set recursive to true for directories. Protected system paths cannot be deleted.")]
    [Parameters(typeof(DeleteFileArgs))]
    public async Task<object> DeleteFile(ServiceConfig config, DeleteFileArgs request)
    {
        var orgId = RequireOrganizationId(request.OrganizationId);
        ValidateWorkspaceId(request.WorkspaceId);
        if (string.IsNullOrWhiteSpace(request.FilePath))
            throw new ArgumentException("Missing required parameter: filePath");

        var normalizedPath = request.FilePath.TrimEnd('/');
        if (ProtectedPaths.Contains(normalizedPath))
            throw new ArgumentException($"Cannot delete protected path: {request.FilePath}");

        await _logger(LogLevel.Info, $"[FileAccess] org={orgId} workspace={request.WorkspaceId} action=delete path={request.FilePath} recursive={request.Recursive ?? false}");

        await _sandbox.DeleteFileAsync(orgId, request.WorkspaceId, request.FilePath, request.Recursive ?? false);

        return new
        {
            Success = true,
            WorkspaceId = request.WorkspaceId,
            Path = request.FilePath,
            Message = "Deleted successfully"
        };
    }

    // ───────────────────────────── Execution ─────────────────────────────

    [Display(Name = "workspace_exec")]
    [Description("Execute a shell command in a workspace via /bin/bash -c. Returns stdout, stderr, and exit code. No network access. Maximum timeout is 300 seconds.")]
    [Parameters(typeof(ExecCommandArgs))]
    public async Task<object> ExecCommand(ServiceConfig config, ExecCommandArgs request)
    {
        var orgId = RequireOrganizationId(request.OrganizationId);
        ValidateWorkspaceId(request.WorkspaceId);
        if (string.IsNullOrWhiteSpace(request.Command))
            throw new ArgumentException("Missing required parameter: command");

        var timeout = Math.Min(request.TimeoutSeconds ?? 30, 300);

        await _logger(LogLevel.Info, $"[FileAccess] org={orgId} workspace={request.WorkspaceId} action=exec command={TruncateForLog(request.Command)}");

        var (stdout, stderr, exitCode) = await _sandbox.ExecAsync(
            orgId, request.WorkspaceId, request.Command, request.WorkingDirectory, timeout);

        return new
        {
            Success = exitCode == 0,
            WorkspaceId = request.WorkspaceId,
            ExitCode = exitCode,
            Stdout = stdout,
            Stderr = stderr
        };
    }

    [Display(Name = "workspace_exec_script")]
    [Description("Write a script to the workspace and execute it. Supports Python and Node.js. The script file is cleaned up after execution. No network access.")]
    [Parameters(typeof(ExecScriptArgs))]
    public async Task<object> ExecScript(ServiceConfig config, ExecScriptArgs request)
    {
        var orgId = RequireOrganizationId(request.OrganizationId);
        ValidateWorkspaceId(request.WorkspaceId);
        if (string.IsNullOrWhiteSpace(request.ScriptContent))
            throw new ArgumentException("Missing required parameter: scriptContent");
        if (string.IsNullOrWhiteSpace(request.ScriptType))
            throw new ArgumentException("Missing required parameter: scriptType");

        var scriptType = request.ScriptType.ToLowerInvariant();
        var (extension, interpreter) = scriptType switch
        {
            "python" => (".py", "python3"),
            "node" => (".js", "node"),
            _ => throw new ArgumentException($"Unsupported script type: {request.ScriptType}. Use 'python' or 'node'.")
        };

        var scriptName = $"hq_script_{Guid.NewGuid():N}{extension}";
        var scriptPath = $"/tmp/{scriptName}";
        var timeout = Math.Min(request.TimeoutSeconds ?? 30, 300);

        await _logger(LogLevel.Info, $"[FileAccess] org={orgId} workspace={request.WorkspaceId} action=exec_script type={scriptType}");

        // Write script to /tmp via exec+base64 — the Docker archive API can reject
        // writes on read-only rootfs containers even when the target is a writable tmpfs.
        var scriptBytes = Encoding.UTF8.GetBytes(request.ScriptContent);
        await _sandbox.WriteFileViaExecAsync(orgId, request.WorkspaceId, scriptPath, scriptBytes);

        try
        {
            // Execute via interpreter (since /tmp is noexec)
            var command = $"{interpreter} {scriptPath}";
            var (stdout, stderr, exitCode) = await _sandbox.ExecAsync(
                orgId, request.WorkspaceId, command, "/workspace", timeout);

            return new
            {
                Success = exitCode == 0,
                WorkspaceId = request.WorkspaceId,
                ScriptType = scriptType,
                ExitCode = exitCode,
                Stdout = stdout,
                Stderr = stderr
            };
        }
        finally
        {
            // Cleanup script
            try
            {
                await _sandbox.DeleteFileAsync(orgId, request.WorkspaceId, scriptPath, false);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    // ───────────────────────────── Cross-Workspace ─────────────────────────────

    [Display(Name = "workspace_copy_between")]
    [Description("Copy a file from one workspace to another. Both workspaces must be running. Reads the file from the source and writes it to the destination.")]
    [Parameters(typeof(CopyBetweenWorkspacesArgs))]
    public async Task<object> CopyBetweenWorkspaces(ServiceConfig config, CopyBetweenWorkspacesArgs request)
    {
        var orgId = RequireOrganizationId(request.OrganizationId);
        if (string.IsNullOrWhiteSpace(request.SourceWorkspaceId))
            throw new ArgumentException("Missing required parameter: sourceWorkspaceId");
        if (string.IsNullOrWhiteSpace(request.DestWorkspaceId))
            throw new ArgumentException("Missing required parameter: destWorkspaceId");
        if (string.IsNullOrWhiteSpace(request.SourcePath))
            throw new ArgumentException("Missing required parameter: sourcePath");
        if (string.IsNullOrWhiteSpace(request.DestPath))
            throw new ArgumentException("Missing required parameter: destPath");

        ValidateWorkspaceId(request.SourceWorkspaceId);
        ValidateWorkspaceId(request.DestWorkspaceId);

        await _logger(LogLevel.Info,
            $"[FileAccess] org={orgId} action=copy_between source={request.SourceWorkspaceId}:{request.SourcePath} dest={request.DestWorkspaceId}:{request.DestPath}");

        // Both workspaces are resolved under the SAME caller-scoped org id — a single tool call
        // must never be able to bridge two different tenants' workspaces.
        var (_, content) = await _sandbox.ReadFileAsync(orgId, request.SourceWorkspaceId, request.SourcePath);

        await _sandbox.WriteFileAsync(orgId, request.DestWorkspaceId, request.DestPath, content);

        return new
        {
            Success = true,
            SourceWorkspaceId = request.SourceWorkspaceId,
            SourcePath = request.SourcePath,
            DestWorkspaceId = request.DestWorkspaceId,
            DestPath = request.DestPath,
            BytesCopied = content.Length
        };
    }
}
