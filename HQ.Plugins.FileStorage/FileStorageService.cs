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

    // ───────────────────────────── Workspace Lifecycle ─────────────────────────────

    [Display(Name = "workspace_create")]
    [Description("Create a new persistent Docker workspace with Python 3, Node.js, and common CLI tools pre-installed. No network access. Files persist across restarts via Docker volumes. Optionally specify a teamId to mount a shared volume at /shared for cross-workspace collaboration.")]
    [Parameters("""{"type":"object","properties":{"workspaceId":{"type":"string","description":"Unique workspace identifier (alphanumeric and hyphens only)"},"teamId":{"type":"string","description":"Optional team ID. Workspaces with the same teamId share a read-write volume mounted at /shared."}},"required":["workspaceId"]}""")]
    public async Task<object> CreateWorkspace(ServiceConfig config, ServiceRequest request)
    {
        ValidateWorkspaceId(request.WorkspaceId);

        await _logger(LogLevel.Info, $"[FileAccess] workspace={request.WorkspaceId} action=create teamId={request.TeamId ?? "none"}");

        return await _sandbox.CreateWorkspaceAsync(request.WorkspaceId, request.TeamId);
    }

    [Display(Name = "workspace_destroy")]
    [Description("Stop and remove a workspace container and its data volume. Team shared volumes are preserved. This action is irreversible.")]
    [Parameters("""{"type":"object","properties":{"workspaceId":{"type":"string","description":"The workspace ID to destroy"}},"required":["workspaceId"]}""")]
    public async Task<object> DestroyWorkspace(ServiceConfig config, ServiceRequest request)
    {
        ValidateWorkspaceId(request.WorkspaceId);

        await _logger(LogLevel.Info, $"[FileAccess] workspace={request.WorkspaceId} action=destroy");

        return await _sandbox.DestroyWorkspaceAsync(request.WorkspaceId);
    }

    [Display(Name = "workspace_list")]
    [Description("List all HQ workspaces with their IDs, team associations, and current status.")]
    [Parameters("""{"type":"object","properties":{}}""")]
    public async Task<object> ListWorkspaces(ServiceConfig config, ServiceRequest request)
    {
        await _logger(LogLevel.Info, "[FileAccess] action=list_workspaces");

        return await _sandbox.ListWorkspacesAsync();
    }

    [Display(Name = "workspace_status")]
    [Description("Get detailed status of a workspace including container state, mounts, and resource configuration.")]
    [Parameters("""{"type":"object","properties":{"workspaceId":{"type":"string","description":"The workspace ID to inspect"}},"required":["workspaceId"]}""")]
    public async Task<object> GetWorkspaceStatus(ServiceConfig config, ServiceRequest request)
    {
        ValidateWorkspaceId(request.WorkspaceId);

        await _logger(LogLevel.Info, $"[FileAccess] workspace={request.WorkspaceId} action=status");

        return await _sandbox.GetStatusAsync(request.WorkspaceId);
    }

    // ───────────────────────────── File Operations ─────────────────────────────

    [Display(Name = "workspace_write_file")]
    [Description("Write a file to a workspace. Content can be plain text or base64-encoded binary. Parent directories are created automatically. Files are written to the persistent /workspace volume.")]
    [Parameters("""{"type":"object","properties":{"workspaceId":{"type":"string","description":"The workspace ID"},"filePath":{"type":"string","description":"Absolute path inside the container (e.g. /workspace/script.py)"},"fileContent":{"type":"string","description":"The file content (text or base64-encoded)"},"isBase64":{"type":"boolean","description":"Set to true if fileContent is base64-encoded binary data. Defaults to false."}},"required":["workspaceId","filePath","fileContent"]}""")]
    public async Task<object> WriteFile(ServiceConfig config, ServiceRequest request)
    {
        ValidateWorkspaceId(request.WorkspaceId);
        if (string.IsNullOrWhiteSpace(request.FilePath))
            throw new ArgumentException("Missing required parameter: filePath");
        if (request.FileContent == null)
            throw new ArgumentException("Missing required parameter: fileContent");

        var content = request.IsBase64 == true
            ? Convert.FromBase64String(request.FileContent)
            : Encoding.UTF8.GetBytes(request.FileContent);

        await _logger(LogLevel.Info, $"[FileAccess] workspace={request.WorkspaceId} action=write path={request.FilePath} bytes={content.Length}");

        await _sandbox.WriteFileAsync(request.WorkspaceId, request.FilePath, content);

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
    [Parameters("""{"type":"object","properties":{"workspaceId":{"type":"string","description":"The workspace ID"},"filePath":{"type":"string","description":"Absolute path inside the container (e.g. /workspace/output.txt)"}},"required":["workspaceId","filePath"]}""")]
    public async Task<object> ReadFile(ServiceConfig config, ServiceRequest request)
    {
        ValidateWorkspaceId(request.WorkspaceId);
        if (string.IsNullOrWhiteSpace(request.FilePath))
            throw new ArgumentException("Missing required parameter: filePath");

        await _logger(LogLevel.Info, $"[FileAccess] workspace={request.WorkspaceId} action=read path={request.FilePath}");

        var (fileName, content) = await _sandbox.ReadFileAsync(request.WorkspaceId, request.FilePath);

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
    [Parameters("""{"type":"object","properties":{"workspaceId":{"type":"string","description":"The workspace ID"},"filePath":{"type":"string","description":"Path to list (defaults to /workspace)"}},"required":["workspaceId"]}""")]
    public async Task<object> ListFiles(ServiceConfig config, ServiceRequest request)
    {
        ValidateWorkspaceId(request.WorkspaceId);

        var path = string.IsNullOrWhiteSpace(request.FilePath) ? "/workspace" : request.FilePath;

        await _logger(LogLevel.Info, $"[FileAccess] workspace={request.WorkspaceId} action=list path={path}");

        var listing = await _sandbox.ListFilesAsync(request.WorkspaceId, path);

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
    [Parameters("""{"type":"object","properties":{"workspaceId":{"type":"string","description":"The workspace ID"},"filePath":{"type":"string","description":"Path to delete"},"recursive":{"type":"boolean","description":"Set to true to recursively delete a directory. Defaults to false."}},"required":["workspaceId","filePath"]}""")]
    public async Task<object> DeleteFile(ServiceConfig config, ServiceRequest request)
    {
        ValidateWorkspaceId(request.WorkspaceId);
        if (string.IsNullOrWhiteSpace(request.FilePath))
            throw new ArgumentException("Missing required parameter: filePath");

        var normalizedPath = request.FilePath.TrimEnd('/');
        if (ProtectedPaths.Contains(normalizedPath))
            throw new ArgumentException($"Cannot delete protected path: {request.FilePath}");

        await _logger(LogLevel.Info, $"[FileAccess] workspace={request.WorkspaceId} action=delete path={request.FilePath} recursive={request.Recursive ?? false}");

        await _sandbox.DeleteFileAsync(request.WorkspaceId, request.FilePath, request.Recursive ?? false);

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
    [Parameters("""{"type":"object","properties":{"workspaceId":{"type":"string","description":"The workspace ID"},"command":{"type":"string","description":"The shell command to execute"},"workingDirectory":{"type":"string","description":"Working directory inside the container (defaults to /workspace)"},"timeoutSeconds":{"type":"integer","description":"Timeout in seconds (default 30, max 300)"}},"required":["workspaceId","command"]}""")]
    public async Task<object> ExecCommand(ServiceConfig config, ServiceRequest request)
    {
        ValidateWorkspaceId(request.WorkspaceId);
        if (string.IsNullOrWhiteSpace(request.Command))
            throw new ArgumentException("Missing required parameter: command");

        var timeout = Math.Min(request.TimeoutSeconds ?? 30, 300);

        await _logger(LogLevel.Info, $"[FileAccess] workspace={request.WorkspaceId} action=exec command={request.Command}");

        var (stdout, stderr, exitCode) = await _sandbox.ExecAsync(
            request.WorkspaceId, request.Command, request.WorkingDirectory, timeout);

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
    [Parameters("""{"type":"object","properties":{"workspaceId":{"type":"string","description":"The workspace ID"},"scriptContent":{"type":"string","description":"The script source code"},"scriptType":{"type":"string","description":"Script type: 'python' or 'node'"},"timeoutSeconds":{"type":"integer","description":"Timeout in seconds (default 30, max 300)"}},"required":["workspaceId","scriptContent","scriptType"]}""")]
    public async Task<object> ExecScript(ServiceConfig config, ServiceRequest request)
    {
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

        await _logger(LogLevel.Info, $"[FileAccess] workspace={request.WorkspaceId} action=exec_script type={scriptType}");

        // Write script to /tmp via exec+base64 — the Docker archive API can reject
        // writes on read-only rootfs containers even when the target is a writable tmpfs.
        var scriptBytes = Encoding.UTF8.GetBytes(request.ScriptContent);
        await _sandbox.WriteFileViaExecAsync(request.WorkspaceId, scriptPath, scriptBytes);

        try
        {
            // Execute via interpreter (since /tmp is noexec)
            var command = $"{interpreter} {scriptPath}";
            var (stdout, stderr, exitCode) = await _sandbox.ExecAsync(
                request.WorkspaceId, command, "/workspace", timeout);

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
                await _sandbox.DeleteFileAsync(request.WorkspaceId, scriptPath, false);
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
    [Parameters("""{"type":"object","properties":{"sourceWorkspaceId":{"type":"string","description":"The source workspace ID"},"sourcePath":{"type":"string","description":"File path in the source workspace"},"destWorkspaceId":{"type":"string","description":"The destination workspace ID"},"destPath":{"type":"string","description":"File path in the destination workspace"}},"required":["sourceWorkspaceId","sourcePath","destWorkspaceId","destPath"]}""")]
    public async Task<object> CopyBetweenWorkspaces(ServiceConfig config, ServiceRequest request)
    {
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
            $"[FileAccess] action=copy_between source={request.SourceWorkspaceId}:{request.SourcePath} dest={request.DestWorkspaceId}:{request.DestPath}");

        // Read from source
        var (_, content) = await _sandbox.ReadFileAsync(request.SourceWorkspaceId, request.SourcePath);

        // Write to destination
        await _sandbox.WriteFileAsync(request.DestWorkspaceId, request.DestPath, content);

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
