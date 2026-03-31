using Docker.DotNet;
using Docker.DotNet.Models;
using ServiceConfig = HQ.Plugins.FileStorage.Models.ServiceConfig;

namespace HQ.Plugins.FileStorage;

internal class DockerSandbox
{
    private readonly DockerClient _client;
    private readonly ServiceConfig _config;

    private const string LabelPlugin = "hq.plugin";
    private const string LabelWorkspaceId = "hq.workspace.id";
    private const string LabelTeamId = "hq.team.id";
    private const string LabelCreated = "hq.created";

    public DockerSandbox(ServiceConfig config)
    {
        _config = config;

        if (!string.IsNullOrWhiteSpace(config.DockerHost))
        {
            _client = new DockerClientConfiguration(new Uri(config.DockerHost)).CreateClient();
        }
        else
        {
            // Auto-detect: named pipe on Windows, unix socket on Linux
            var uri = OperatingSystem.IsWindows()
                ? new Uri("npipe://./pipe/docker_engine")
                : new Uri("unix:///var/run/docker.sock");
            _client = new DockerClientConfiguration(uri).CreateClient();
        }
    }

    private static string ContainerName(string workspaceId) => $"hq-workspace-{workspaceId}";
    private static string VolumeName(string workspaceId) => $"hq-workspace-{workspaceId}-data";
    private static string TeamVolumeName(string teamId) => $"hq-team-{teamId}";

    public async Task<object> CreateWorkspaceAsync(string workspaceId, string teamId)
    {
        var containerName = ContainerName(workspaceId);

        // Verify the Docker image exists before attempting container creation
        try
        {
            await _client.Images.InspectImageAsync(_config.DefaultImage);
        }
        catch (DockerImageNotFoundException)
        {
            throw new InvalidOperationException(
                $"Docker image '{_config.DefaultImage}' not found. " +
                "Build it with: docker build -t hq-workspace:latest -f HQ.Plugins.FileStorage/Dockerfile HQ.Plugins.FileStorage/");
        }

        // Check if container already exists
        var existing = await FindContainerAsync(workspaceId);
        if (existing != null)
            throw new InvalidOperationException($"Workspace '{workspaceId}' already exists (state: {existing.State})");

        // Create workspace volume
        var wsVolumeName = VolumeName(workspaceId);
        await _client.Volumes.CreateAsync(new VolumesCreateParameters { Name = wsVolumeName });

        // Build mount list
        var mounts = new List<Mount>
        {
            new()
            {
                Type = "volume",
                Source = wsVolumeName,
                Target = "/workspace"
            }
        };

        // Team shared volume
        if (!string.IsNullOrWhiteSpace(teamId))
        {
            var teamVolName = TeamVolumeName(teamId);
            await _client.Volumes.CreateAsync(new VolumesCreateParameters { Name = teamVolName });
            mounts.Add(new Mount
            {
                Type = "volume",
                Source = teamVolName,
                Target = "/shared"
            });
        }

        // Tmpfs mounts for /tmp and /run
        var tmpfsMounts = new Dictionary<string, string>
        {
            ["/tmp"] = $"size={_config.WorkspaceSizeMb}m,noexec",
            ["/run"] = "size=16m"
        };

        var labels = new Dictionary<string, string>
        {
            [LabelPlugin] = "FileStorage",
            [LabelWorkspaceId] = workspaceId,
            [LabelCreated] = DateTime.UtcNow.ToString("O")
        };
        if (!string.IsNullOrWhiteSpace(teamId))
            labels[LabelTeamId] = teamId;

        var memoryBytes = _config.MemoryLimitMb * 1024 * 1024;

        var createParams = new CreateContainerParameters
        {
            Image = _config.DefaultImage,
            Name = containerName,
            Labels = labels,
            HostConfig = new HostConfig
            {
                Mounts = mounts,
                Tmpfs = tmpfsMounts,
                NetworkMode = "none",
                ReadonlyRootfs = true,
                CapDrop = new List<string> { "ALL" },
                SecurityOpt = new List<string> { "no-new-privileges=true" },
                Memory = memoryBytes,
                MemorySwap = memoryBytes,
                PidsLimit = _config.PidsLimit,
                CPUShares = _config.CpuShares,
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No }
            }
        };

        var response = await _client.Containers.CreateContainerAsync(createParams);
        await _client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters());

        return new
        {
            Success = true,
            WorkspaceId = workspaceId,
            ContainerId = response.ID,
            TeamId = teamId,
            Message = $"Workspace '{workspaceId}' created and running"
        };
    }

    public async Task<object> DestroyWorkspaceAsync(string workspaceId)
    {
        var containerName = ContainerName(workspaceId);

        // Stop container (ignore if already stopped)
        try
        {
            await _client.Containers.StopContainerAsync(containerName, new ContainerStopParameters { WaitBeforeKillSeconds = 5 });
        }
        catch (DockerContainerNotFoundException) { }

        // Remove container
        try
        {
            await _client.Containers.RemoveContainerAsync(containerName, new ContainerRemoveParameters { Force = true });
        }
        catch (DockerContainerNotFoundException) { }

        // Remove workspace volume (not team volumes)
        try
        {
            await _client.Volumes.RemoveAsync(VolumeName(workspaceId));
        }
        catch (DockerApiException) { }

        return new
        {
            Success = true,
            WorkspaceId = workspaceId,
            Message = $"Workspace '{workspaceId}' destroyed (team volumes preserved)"
        };
    }

    public async Task<object> ListWorkspacesAsync()
    {
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { [$"{LabelPlugin}=FileStorage"] = true }
            }
        });

        var workspaces = containers.Select(c => new
        {
            WorkspaceId = GetLabel(c.Labels, LabelWorkspaceId, "unknown"),
            TeamId = GetLabel(c.Labels, LabelTeamId),
            Status = c.State,
            Created = GetLabel(c.Labels, LabelCreated),
            ContainerName = c.Names.FirstOrDefault()?.TrimStart('/')
        }).ToList();

        return new { Workspaces = workspaces };
    }

    public async Task<object> GetStatusAsync(string workspaceId)
    {
        var containerName = ContainerName(workspaceId);
        var inspect = await _client.Containers.InspectContainerAsync(containerName);

        return new
        {
            WorkspaceId = workspaceId,
            State = inspect.State.Status,
            Running = inspect.State.Running,
            StartedAt = inspect.State.StartedAt,
            Image = inspect.Config.Image,
            Mounts = inspect.Mounts.Select(m => new
            {
                Source = m.Name ?? m.Source,
                Destination = m.Destination,
                m.Type,
                m.RW
            }),
            Memory = $"{_config.MemoryLimitMb}MB",
            CpuShares = _config.CpuShares,
            PidsLimit = _config.PidsLimit,
            NetworkMode = "none"
        };
    }

    public async Task<(string Stdout, string Stderr, long ExitCode)> ExecAsync(
        string workspaceId, string command, string workingDir, int timeoutSeconds)
    {
        var containerName = ContainerName(workspaceId);

        var execParams = new ContainerExecCreateParameters
        {
            Cmd = new List<string> { "/bin/bash", "-c", command },
            AttachStdout = true,
            AttachStderr = true,
            WorkingDir = string.IsNullOrWhiteSpace(workingDir) ? "/workspace" : workingDir
        };

        var exec = await _client.Exec.ExecCreateContainerAsync(containerName, execParams);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        using var stream = await _client.Exec.StartAndAttachContainerExecAsync(exec.ID, false, cts.Token);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(cts.Token);

        var inspectExec = await _client.Exec.InspectContainerExecAsync(exec.ID);

        return (stdout, stderr, inspectExec.ExitCode);
    }

    public async Task WriteFileAsync(string workspaceId, string containerPath, byte[] content)
    {
        var containerName = ContainerName(workspaceId);
        var dir = GetDirectoryPath(containerPath);
        var fileName = Path.GetFileName(containerPath);

        // Ensure parent directory exists
        if (dir != "/workspace")
        {
            await ExecAsync(workspaceId, $"mkdir -p {dir}", "/", 10);
        }

        using var tar = TarHelper.CreateTarWithFile(fileName, content);
        await _client.Containers.ExtractArchiveToContainerAsync(containerName, new ContainerPathStatParameters
        {
            Path = dir
        }, tar);
    }

    /// <summary>
    /// Write file content via exec + base64, bypassing the Docker archive API.
    /// Use this for tmpfs paths where ExtractArchiveToContainerAsync may fail
    /// on read-only rootfs containers despite the target being a writable mount.
    /// Base64 output contains only [A-Za-z0-9+/=\n] — no shell metacharacters.
    /// </summary>
    public async Task WriteFileViaExecAsync(string workspaceId, string containerPath, byte[] content)
    {
        var b64 = Convert.ToBase64String(content);
        var (_, stderr, exitCode) = await ExecAsync(
            workspaceId, $"printf '%s' '{b64}' | base64 -d > {containerPath}", "/", 10);

        if (exitCode != 0)
            throw new InvalidOperationException($"Failed to write file via exec (exit {exitCode}): {stderr}");
    }

    public async Task<(string FileName, byte[] Content)> ReadFileAsync(string workspaceId, string containerPath)
    {
        var containerName = ContainerName(workspaceId);

        var response = await _client.Containers.GetArchiveFromContainerAsync(containerName,
            new GetArchiveFromContainerParameters { Path = containerPath }, false);

        return await TarHelper.ExtractFirstFileAsync(response.Stream);
    }

    public async Task<string> ListFilesAsync(string workspaceId, string path)
    {
        var safePath = string.IsNullOrWhiteSpace(path) ? "/workspace" : path;
        var (stdout, stderr, exitCode) = await ExecAsync(workspaceId, $"ls -la {safePath}", "/", 10);
        if (exitCode != 0)
            throw new InvalidOperationException($"ls failed (exit {exitCode}): {stderr}");
        return stdout;
    }

    public async Task DeleteFileAsync(string workspaceId, string path, bool recursive)
    {
        var cmd = recursive ? $"rm -rf {path}" : $"rm -f {path}";
        var (_, stderr, exitCode) = await ExecAsync(workspaceId, cmd, "/", 10);
        if (exitCode != 0)
            throw new InvalidOperationException($"Delete failed (exit {exitCode}): {stderr}");
    }

    private async Task<ContainerListResponse> FindContainerAsync(string workspaceId)
    {
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [ContainerName(workspaceId)] = true }
            }
        });
        return containers.FirstOrDefault();
    }

    private static string GetDirectoryPath(string containerPath)
    {
        var lastSlash = containerPath.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : containerPath[..lastSlash];
    }

    private static string GetLabel(IDictionary<string, string> labels, string key, string defaultValue = null)
    {
        return labels != null && labels.TryGetValue(key, out var value) ? value : defaultValue;
    }
}
