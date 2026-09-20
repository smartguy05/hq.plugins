using System.Security.Cryptography;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using ServiceConfig = HQ.Plugins.FileStorage.Models.ServiceConfig;

namespace HQ.Plugins.FileStorage;

internal class DockerSandbox
{
    // IDockerClient (not the concrete DockerClient) so HQ.Plugins.Tests can substitute a Moq
    // mock and exercise EnsureWorkspaceOwnedByOrgAsync against a mocked Docker daemon — see
    // DockerSandboxOwnershipTests (WP6A-5 adversarial re-review, 2026-09: the ownership gate had
    // no direct test, unlike the analogous ClaudeCode ContainerManager fix).
    private readonly IDockerClient _client;
    private readonly ServiceConfig _config;

    private const string LabelPlugin = "hq.plugin";
    private const string LabelWorkspaceId = "hq.workspace.id";
    private const string LabelTeamId = "hq.team.id";
    private const string LabelCreated = "hq.created";

    /// <summary>WP6A-5: the caller-scoped tenant that owns this container/volume.</summary>
    internal const string LabelOrgId = "hq.org.id";

    public DockerSandbox(ServiceConfig config) : this(config, CreateRealClient(config))
    {
    }

    /// <summary>Test-only entry point: lets HQ.Plugins.Tests substitute a mocked
    /// <see cref="IDockerClient"/> so ownership-binding behavior can be asserted without a live
    /// Docker daemon.</summary>
    internal DockerSandbox(ServiceConfig config, IDockerClient client)
    {
        _config = config;
        _client = client;
    }

    private static IDockerClient CreateRealClient(ServiceConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.DockerHost))
        {
            return new DockerClientConfiguration(new Uri(config.DockerHost)).CreateClient();
        }

        // Auto-detect: named pipe on Windows, unix socket on Linux
        var uri = OperatingSystem.IsWindows()
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");
        return new DockerClientConfiguration(uri).CreateClient();
    }

    /// <summary>
    /// WP6A-5: a short, Docker-name-safe, deterministic tag derived from the caller's
    /// organization id. Container/volume names are built from {orgTag}-{callerSuppliedId}
    /// (hashed rather than embedding the raw GUID, to keep names short) so that two different
    /// orgs supplying the SAME workspaceId/teamId text can never collide on the same Docker
    /// resource — they are namespaced apart before any label check even runs.
    /// </summary>
    internal static string OrgTag(Guid organizationId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(organizationId.ToString("N")));
        return Convert.ToHexStringLower(hash)[..12];
    }

    internal static string ContainerName(Guid organizationId, string workspaceId) =>
        $"hq-workspace-{OrgTag(organizationId)}-{workspaceId}";

    internal static string VolumeName(Guid organizationId, string workspaceId) =>
        $"hq-workspace-{OrgTag(organizationId)}-{workspaceId}-data";

    internal static string TeamVolumeName(Guid organizationId, string teamId) =>
        $"hq-team-{OrgTag(organizationId)}-{teamId}";

    /// <summary>
    /// WP6A-5: verifies the named container both exists and carries an <c>hq.org.id</c> label
    /// matching the caller's organization id before any read/write/exec/destroy proceeds. The
    /// org-scoped naming above already makes cross-org name collisions practically impossible,
    /// but this is the explicit ownership check the finding calls for, and it fails CLOSED
    /// (treats "not found" and "found but wrong org" identically, so a probing caller cannot
    /// distinguish "doesn't exist" from "exists but isn't yours").
    /// </summary>
    private async Task EnsureWorkspaceOwnedByOrgAsync(Guid organizationId, string containerName)
    {
        var container = await FindContainerByExactNameAsync(containerName);
        if (container == null || !string.Equals(GetLabel(container.Labels, LabelOrgId), organizationId.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Workspace not found.");
    }

    public async Task<object> CreateWorkspaceAsync(Guid organizationId, string workspaceId, string teamId)
    {
        var containerName = ContainerName(organizationId, workspaceId);

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

        // Check if container already exists (scoped to this org's namespace)
        var existing = await FindContainerByExactNameAsync(containerName);
        if (existing != null)
            throw new InvalidOperationException($"Workspace '{workspaceId}' already exists (state: {existing.State})");

        // Create workspace volume
        var wsVolumeName = VolumeName(organizationId, workspaceId);
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
            var teamVolName = TeamVolumeName(organizationId, teamId);
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

        // WP6A-5: stamp the owning org on the container so every later operation can verify
        // ownership via EnsureWorkspaceOwnedByOrgAsync before touching it.
        var labels = new Dictionary<string, string>
        {
            [LabelPlugin] = "FileStorage",
            [LabelWorkspaceId] = workspaceId,
            [LabelOrgId] = organizationId.ToString(),
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

    public async Task<object> DestroyWorkspaceAsync(Guid organizationId, string workspaceId)
    {
        var containerName = ContainerName(organizationId, workspaceId);

        // WP6A-5: verify ownership BEFORE any destructive call — a cross-org destroy must fail
        // closed with "not found", never silently succeed or reveal that the resource exists.
        await EnsureWorkspaceOwnedByOrgAsync(organizationId, containerName);

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
            await _client.Volumes.RemoveAsync(VolumeName(organizationId, workspaceId));
        }
        catch (DockerApiException) { }

        return new
        {
            Success = true,
            WorkspaceId = workspaceId,
            Message = $"Workspace '{workspaceId}' destroyed (team volumes preserved)"
        };
    }

    public async Task<object> ListWorkspacesAsync(Guid organizationId)
    {
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = BuildOrgScopedListFilters(organizationId)
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

    /// <summary>
    /// WP6A-5: workspace_list must never return another org's workspaces. Filtering happens
    /// server-side via the Docker label filter (not a client-side Where), so containers
    /// belonging to other tenants never even cross the wire. Split out as a pure function so it
    /// is directly unit-testable without a Docker daemon.
    /// </summary>
    internal static Dictionary<string, IDictionary<string, bool>> BuildOrgScopedListFilters(Guid organizationId) =>
        new()
        {
            ["label"] = new Dictionary<string, bool>
            {
                [$"{LabelPlugin}=FileStorage"] = true,
                [$"{LabelOrgId}={organizationId}"] = true
            }
        };

    public async Task<object> GetStatusAsync(Guid organizationId, string workspaceId)
    {
        var containerName = ContainerName(organizationId, workspaceId);

        await EnsureWorkspaceOwnedByOrgAsync(organizationId, containerName);

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

    public Task<(string Stdout, string Stderr, long ExitCode)> ExecAsync(
        Guid organizationId, string workspaceId, string command, string workingDir, int timeoutSeconds)
        => ExecCmdAsync(organizationId, workspaceId, new List<string> { "/bin/bash", "-c", command }, workingDir, timeoutSeconds);

    /// <summary>
    /// Runs <paramref name="argv"/> directly (no shell), so an element such as a caller-supplied
    /// file path cannot be reinterpreted as shell syntax no matter what characters it contains.
    ///
    /// WP6A-11 (blocking, adversarial re-review, 2026-09): <see cref="ListFilesAsync"/>,
    /// <see cref="DeleteFileAsync"/> and the mkdir-p call in <see cref="WriteFileAsync"/>
    /// previously built a `/bin/bash -c "ls -la {path}"` / `"rm -rf {path}"` / `"mkdir -p {dir}"`
    /// string from the caller-supplied, unvalidated path — FilePath is only checked for
    /// non-emptiness in FileStorageService.cs (unlike WorkspaceId), so a path such as
    /// `"x; cat /etc/shadow #"` achieved arbitrary command execution inside the sandbox
    /// container, defeating (not merely bypassing) the ProtectedPaths check. Mirrors
    /// HQ.Plugins.ClaudeCode.ContainerManager.ExecArgvAsync (WP6A-3).
    /// </summary>
    public Task<(string Stdout, string Stderr, long ExitCode)> ExecArgvAsync(
        Guid organizationId, string workspaceId, IReadOnlyList<string> argv, string workingDir, int timeoutSeconds)
        => ExecCmdAsync(organizationId, workspaceId, argv.ToList(), workingDir, timeoutSeconds);

    private async Task<(string Stdout, string Stderr, long ExitCode)> ExecCmdAsync(
        Guid organizationId, string workspaceId, List<string> cmd, string workingDir, int timeoutSeconds)
    {
        var containerName = ContainerName(organizationId, workspaceId);

        await EnsureWorkspaceOwnedByOrgAsync(organizationId, containerName);

        var execParams = new ContainerExecCreateParameters
        {
            Cmd = cmd,
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

    /// <summary>Pure argv builders for the three exec calls that previously interpolated a raw
    /// path into a bash -c string (WP6A-11). Split out so they are directly unit-testable without
    /// a Docker daemon: no matter what shell metacharacters <paramref name="path"/>/<paramref name="dir"/>
    /// contain, they arrive at the container's exec as a single argv element, never shell text.</summary>
    internal static List<string> BuildLsArgv(string path) => new() { "ls", "-la", path };

    internal static List<string> BuildRmArgv(string path, bool recursive) =>
        recursive ? new List<string> { "rm", "-rf", path } : new List<string> { "rm", "-f", path };

    internal static List<string> BuildMkdirPArgv(string dir) => new() { "mkdir", "-p", dir };

    public async Task WriteFileAsync(Guid organizationId, string workspaceId, string containerPath, byte[] content)
    {
        var containerName = ContainerName(organizationId, workspaceId);

        await EnsureWorkspaceOwnedByOrgAsync(organizationId, containerName);

        var dir = GetDirectoryPath(containerPath);
        var fileName = Path.GetFileName(containerPath);

        // Ensure parent directory exists
        if (dir != "/workspace")
        {
            await ExecArgvAsync(organizationId, workspaceId, BuildMkdirPArgv(dir), "/", 10);
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
    public async Task WriteFileViaExecAsync(Guid organizationId, string workspaceId, string containerPath, byte[] content)
    {
        var b64 = Convert.ToBase64String(content);
        var (_, stderr, exitCode) = await ExecAsync(
            organizationId, workspaceId, $"printf '%s' '{b64}' | base64 -d > {containerPath}", "/", 10);

        if (exitCode != 0)
            throw new InvalidOperationException($"Failed to write file via exec (exit {exitCode}): {stderr}");
    }

    public async Task<(string FileName, byte[] Content)> ReadFileAsync(Guid organizationId, string workspaceId, string containerPath)
    {
        var containerName = ContainerName(organizationId, workspaceId);

        await EnsureWorkspaceOwnedByOrgAsync(organizationId, containerName);

        var response = await _client.Containers.GetArchiveFromContainerAsync(containerName,
            new GetArchiveFromContainerParameters { Path = containerPath }, false);

        return await TarHelper.ExtractFirstFileAsync(response.Stream);
    }

    public async Task<string> ListFilesAsync(Guid organizationId, string workspaceId, string path)
    {
        var safePath = string.IsNullOrWhiteSpace(path) ? "/workspace" : path;
        var (stdout, stderr, exitCode) = await ExecArgvAsync(organizationId, workspaceId, BuildLsArgv(safePath), "/", 10);
        if (exitCode != 0)
            throw new InvalidOperationException($"ls failed (exit {exitCode}): {stderr}");
        return stdout;
    }

    public async Task DeleteFileAsync(Guid organizationId, string workspaceId, string path, bool recursive)
    {
        var argv = BuildRmArgv(path, recursive);
        var (_, stderr, exitCode) = await ExecArgvAsync(organizationId, workspaceId, argv, "/", 10);
        if (exitCode != 0)
            throw new InvalidOperationException($"Delete failed (exit {exitCode}): {stderr}");
    }

    private async Task<ContainerListResponse> FindContainerByExactNameAsync(string containerName)
    {
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [containerName] = true }
            }
        });
        // Docker's "name" filter is a substring match, not exact — require an exact match
        // against the container's own name so e.g. "hq-workspace-abc" can't be matched by a
        // filter for "hq-workspace-ab".
        return containers.FirstOrDefault(c => c.Names.Any(n => n.TrimStart('/') == containerName));
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
