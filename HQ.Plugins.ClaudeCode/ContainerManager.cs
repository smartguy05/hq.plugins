using System.Security.Cryptography;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using ServiceConfig = HQ.Plugins.ClaudeCode.Models.ServiceConfig;

namespace HQ.Plugins.ClaudeCode;

internal class ContainerManager
{
    // IDockerClient (not the concrete DockerClient) so HQ.Plugins.Tests can substitute a Moq
    // mock and exercise the WP6A-6 ownership-binding logic below without a real Docker daemon
    // — see ContainerManagerOwnershipTests. DockerClient (the type CreateClient() returns)
    // implements this interface, so production behavior is unchanged.
    private readonly IDockerClient _client;
    private readonly ServiceConfig _config;
    private readonly string _ownerFingerprint;

    private const string LabelPlugin = "hq.plugin";
    private const string LabelSessionId = "hq.claudecode.session";
    private const string LabelCreated = "hq.created";

    // WP6A-6 (adversarial re-review, 2026-09): the format check on sessionId
    // (GitArgValidation.IsValidSessionId) only rejects malformed/injection-shaped strings —
    // every legitimately generated sessionId already matches ^[a-f0-9]{12}$ for every tenant,
    // so a leaked, well-formed sessionId from another org sailed straight through it. hq.plugins
    // has no ITenantContext/OrgId anywhere (confirmed by grep — the same wall WP6A-5 was
    // deferred behind) and the plugin invocation contract is (ServiceConfig, TArgs) with no
    // caller-identity parameter, so a real OrgId-based binding isn't achievable from this repo
    // alone without a core (HQ.Models/HQ.Services) plumbing change.
    //
    // As the strongest mitigation available without that plumbing, every session is bound to a
    // fingerprint of the ServiceConfig instance that created it (a keyless hash of its fields,
    // not the raw secrets) and stored as this label at creation. Every operation that resolves
    // an existing container (session reuse in EnsureContainerAsync, DestroySession, and the
    // IsSessionAccessibleAsync gate ClaudeCodeService runs before GetDiff/ContinueSession/
    // ReviewChanges/GetStatus/CreatePr) rejects a mismatch as "not found" rather than acting on
    // it. Distinct agents get distinct plugin-config DB rows (Per-Agent Scoping — see
    // CLAUDE.md), so in practice this closes the cited repro: two agents/orgs differ in at
    // least Name/Description/AnthropicApiKey/GitHubToken, so their fingerprints differ. It is a
    // config-identity heuristic, not a cryptographic tenant guarantee — two agents provisioned
    // with byte-identical ServiceConfig values would still collide. Closing that residual case
    // needs the same OrgId plumbing already deferred for WP6A-5.
    private const string LabelOwner = "hq.claudecode.owner";

    // WP6A-3: the sandbox container ran as root with no cap_drop, no read-only rootfs, and the
    // shared default `bridge` network — the same network other containers on the host (including
    // HQ's own Postgres/Redis) may be attached to. Harden it to FileStorage's baseline (CapDrop
    // ALL, ReadonlyRootfs, no-new-privileges) and give it a network of its own instead of the
    // host-wide default bridge.
    internal const string SandboxNetworkName = "hq-claudecode-sandbox";

    // WP6A-3: DockerHost is free-text and settable by a TenantAdmin (or a CanConfigureTools
    // agent via set_tool_config) with no validation. Restrict it to the schemes Docker.DotNet
    // actually understands instead of accepting an arbitrary string.
    private static readonly HashSet<string> AllowedDockerHostSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "unix", "npipe", "tcp", "http", "https" };

    internal static void ValidateDockerHost(string dockerHost)
    {
        if (string.IsNullOrWhiteSpace(dockerHost))
            return;

        if (!Uri.TryCreate(dockerHost, UriKind.Absolute, out var uri) ||
            !AllowedDockerHostSchemes.Contains(uri.Scheme))
        {
            throw new ArgumentException(
                $"DockerHost must be a unix://, npipe://, tcp:// or http(s):// URI. Got: '{dockerHost}'");
        }
    }

    public ContainerManager(ServiceConfig config) : this(config, CreateRealClient(config))
    {
    }

    /// <summary>Test-only entry point (see the <see cref="LabelOwner"/> remarks above): lets
    /// HQ.Plugins.Tests substitute a mocked <see cref="IDockerClient"/> so ownership-binding
    /// behavior can be asserted without a live Docker daemon.</summary>
    internal ContainerManager(ServiceConfig config, IDockerClient client)
    {
        _config = config;
        _client = client;
        _ownerFingerprint = ComputeOwnerFingerprint(config);
    }

    private static IDockerClient CreateRealClient(ServiceConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.DockerHost))
        {
            ValidateDockerHost(config.DockerHost);
            return new DockerClientConfiguration(new Uri(config.DockerHost)).CreateClient();
        }

        var uri = OperatingSystem.IsWindows()
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");
        return new DockerClientConfiguration(uri).CreateClient();
    }

    /// <summary>Deterministic, keyless fingerprint of the fields that identify which
    /// agent/tenant's ServiceConfig produced this ContainerManager. Pure and side-effect-free so
    /// it — and the ownership decisions built on it — can be unit tested without a Docker daemon.
    /// See the <see cref="LabelOwner"/> remarks for why this exists and its limits.</summary>
    internal static string ComputeOwnerFingerprint(ServiceConfig config)
    {
        var canonical = string.Join('',
            config?.Name, config?.Description, config?.DockerHost, config?.DockerImage,
            config?.CloneBaseDir, config?.GitHubHost, config?.AnthropicApiKey, config?.GitHubToken);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>True when <paramref name="labels"/> carries the <see cref="LabelOwner"/> label
    /// with a value matching <paramref name="expectedOwnerFingerprint"/>. False (never an
    /// exception) for missing labels, so a pre-WP6A-6 container with no owner label at all is
    /// treated as foreign rather than trusted.</summary>
    internal static bool IsOwnedBy(IDictionary<string, string> labels, string expectedOwnerFingerprint)
        => labels != null &&
           labels.TryGetValue(LabelOwner, out var owner) &&
           !string.IsNullOrEmpty(owner) &&
           string.Equals(owner, expectedOwnerFingerprint, StringComparison.Ordinal);

    private static string ContainerName(string sessionId) => $"hq-claude-code-{sessionId}";
    private static string VolumeName(string sessionId) => $"hq-claude-code-{sessionId}-data";

    /// <summary>WP6A-6: true when no container exists yet for <paramref name="sessionId"/>
    /// (nothing to protect — a fresh id) or it exists and belongs to this caller's own config.
    /// False only for the actual repro: a well-formed sessionId whose container was created
    /// under a different ServiceConfig (a different agent/tenant). Callers must treat "false"
    /// identically to "the session doesn't exist" — never a distinct "forbidden" — so a probe
    /// can't be used to confirm another org's session id is live.</summary>
    public async Task<bool> IsSessionAccessibleAsync(string sessionId)
    {
        var existing = await FindContainerAsync(sessionId);
        return existing == null || IsOwnedBy(existing.Labels, _ownerFingerprint);
    }

    public async Task<string> EnsureContainerAsync(string sessionId)
    {
        var containerName = ContainerName(sessionId);
        var existing = await FindContainerAsync(sessionId);

        if (existing != null)
        {
            // WP6A-6: a caller can reach here by passing an existing sessionId to RunTask
            // (resume-by-id). Refuse to silently start/reuse a container that belongs to a
            // different ServiceConfig — that would hand a foreign session's checkout and repo
            // access to whoever guessed/leaked the id.
            if (!IsOwnedBy(existing.Labels, _ownerFingerprint))
                throw new InvalidOperationException("sessionId is invalid");

            if (existing.State == "running")
                return existing.ID;

            // Restart stopped container
            await _client.Containers.StartContainerAsync(containerName, new ContainerStartParameters());
            return existing.ID;
        }

        // Create volume
        var volumeName = VolumeName(sessionId);
        await _client.Volumes.CreateAsync(new VolumesCreateParameters { Name = volumeName });

        var envVars = new List<string>
        {
            $"ANTHROPIC_API_KEY={_config.AnthropicApiKey}",
            "CLAUDE_CODE_DISABLE_NONESSENTIAL=1"
        };

        if (!string.IsNullOrWhiteSpace(_config.GitHubToken))
        {
            envVars.Add($"GITHUB_TOKEN={_config.GitHubToken}");
            envVars.Add($"GH_TOKEN={_config.GitHubToken}");
        }

        var labels = new Dictionary<string, string>
        {
            [LabelPlugin] = "ClaudeCode",
            [LabelSessionId] = sessionId,
            [LabelOwner] = _ownerFingerprint,
            [LabelCreated] = DateTime.UtcNow.ToString("O")
        };

        await EnsureSandboxNetworkAsync();

        var hasNetworkFiltering = !string.IsNullOrWhiteSpace(_config.NetworkWhitelist) ||
                                   !string.IsNullOrWhiteSpace(_config.NetworkBlacklist);

        var createParams = BuildCreateContainerParameters(
            _config, containerName, volumeName, labels, envVars, hasNetworkFiltering);

        var response = await _client.Containers.CreateContainerAsync(createParams);
        await _client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters());

        // Apply network filtering if configured
        if (hasNetworkFiltering)
            await ApplyNetworkFilteringAsync(sessionId);

        return response.ID;
    }

    /// <summary>
    /// Builds the container-creation parameters, including the hardened <see cref="HostConfig"/>
    /// (WP6A-3: CapDrop ALL, ReadonlyRootfs, no-new-privileges, and a dedicated network instead
    /// of the shared default `bridge`). Pure and side-effect-free so it can be unit tested
    /// without a Docker daemon.
    /// </summary>
    internal static CreateContainerParameters BuildCreateContainerParameters(
        ServiceConfig config, string containerName, string volumeName,
        Dictionary<string, string> labels, List<string> envVars, bool needsNetworkAdmin)
    {
        var memoryBytes = config.MemoryLimitMb * 1024 * 1024;

        var capAdd = new List<string>();
        if (needsNetworkAdmin)
            capAdd.Add("NET_ADMIN");

        return new CreateContainerParameters
        {
            Image = config.DockerImage,
            Name = containerName,
            Labels = labels,
            Env = envVars,
            Cmd = new List<string> { "sleep", "infinity" },
            HostConfig = new HostConfig
            {
                Mounts = new List<Mount>
                {
                    new()
                    {
                        Type = "volume",
                        Source = volumeName,
                        Target = config.CloneBaseDir
                    }
                },
                // WP6A-3: writes to anything other than the mounted volume and these tmpfs
                // mounts now fail closed instead of silently succeeding on a writable rootfs.
                Tmpfs = new Dictionary<string, string>
                {
                    ["/tmp"] = "size=256m",
                    ["/run"] = "size=16m",
                    // Claude Code / npm / git config and caches live under $HOME (the container
                    // runs as root, so that's /root) — give it somewhere to write that isn't the
                    // container's read-only rootfs, without persisting anything across recreates.
                    ["/root"] = "size=256m"
                },
                ReadonlyRootfs = true,
                CapDrop = new List<string> { "ALL" },
                SecurityOpt = new List<string> { "no-new-privileges=true" },
                NetworkMode = SandboxNetworkName,
                Memory = memoryBytes,
                MemorySwap = memoryBytes,
                PidsLimit = config.PidsLimit,
                CPUShares = config.CpuShares,
                CapAdd = capAdd.Count > 0 ? capAdd : null,
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No }
            }
        };
    }

    /// <summary>Creates <see cref="SandboxNetworkName"/> if it doesn't already exist. Idempotent —
    /// safe to call before every container creation.</summary>
    private async Task EnsureSandboxNetworkAsync()
    {
        var existing = await _client.Networks.ListNetworksAsync(new NetworksListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [SandboxNetworkName] = true }
            }
        });
        if (existing.Any(n => n.Name == SandboxNetworkName))
            return;

        try
        {
            await _client.Networks.CreateNetworkAsync(new NetworksCreateParameters
            {
                Name = SandboxNetworkName,
                Driver = "bridge"
            });
        }
        catch (DockerApiException)
        {
            // Benign race: another concurrent EnsureContainerAsync call created it first.
        }
    }

    public async Task DestroyContainerAsync(string sessionId)
    {
        var containerName = ContainerName(sessionId);

        // WP6A-6: refuse to stop/remove/destroy-the-volume-of a container that belongs to a
        // different ServiceConfig. Without this, "obtain another org's leaked sessionId and call
        // claude_code_destroy_session" was irreversible destruction of that org's session with
        // zero ownership check at all.
        var existing = await FindContainerAsync(sessionId);
        if (existing != null && !IsOwnedBy(existing.Labels, _ownerFingerprint))
            throw new InvalidOperationException("sessionId is invalid");

        try
        {
            await _client.Containers.StopContainerAsync(containerName,
                new ContainerStopParameters { WaitBeforeKillSeconds = 5 });
        }
        catch (DockerContainerNotFoundException) { }

        try
        {
            await _client.Containers.RemoveContainerAsync(containerName,
                new ContainerRemoveParameters { Force = true });
        }
        catch (DockerContainerNotFoundException) { }

        try
        {
            await _client.Volumes.RemoveAsync(VolumeName(sessionId));
        }
        catch (DockerApiException) { }
    }

    public Task<(string Stdout, string Stderr, long ExitCode)> ExecAsync(
        string sessionId, string command, string workingDir, int timeoutSeconds)
        => ExecCmdAsync(sessionId, new List<string> { "/bin/bash", "-c", command }, workingDir, timeoutSeconds);

    /// <summary>
    /// Runs <paramref name="argv"/> directly (no shell), so its elements — e.g. a repo URL or
    /// branch name — cannot be reinterpreted as shell syntax no matter what characters they
    /// contain. WP6A-3: git operations built a `/bin/bash -c "git clone {repoUrl} {cloneDir}"`
    /// string, so an attacker-controlled repoUrl/branch could inject arbitrary shell commands.
    /// </summary>
    public Task<(string Stdout, string Stderr, long ExitCode)> ExecArgvAsync(
        string sessionId, IReadOnlyList<string> argv, string workingDir, int timeoutSeconds)
        => ExecCmdAsync(sessionId, argv.ToList(), workingDir, timeoutSeconds);

    private async Task<(string Stdout, string Stderr, long ExitCode)> ExecCmdAsync(
        string sessionId, List<string> cmd, string workingDir, int timeoutSeconds)
    {
        var containerName = ContainerName(sessionId);

        var execParams = new ContainerExecCreateParameters
        {
            Cmd = cmd,
            AttachStdout = true,
            AttachStderr = true,
            WorkingDir = string.IsNullOrWhiteSpace(workingDir) ? _config.CloneBaseDir : workingDir
        };

        var exec = await _client.Exec.ExecCreateContainerAsync(containerName, execParams);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var stream = await _client.Exec.StartAndAttachContainerExecAsync(exec.ID, false, cts.Token);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(cts.Token);

        var inspectExec = await _client.Exec.InspectContainerExecAsync(exec.ID);
        return (stdout, stderr, inspectExec.ExitCode);
    }

    public async Task<string> GetContainerStatusAsync(string sessionId)
    {
        var existing = await FindContainerAsync(sessionId);
        if (existing == null || !IsOwnedBy(existing.Labels, _ownerFingerprint))
            return "not_found";

        return existing.State;
    }

    private async Task<ContainerListResponse> FindContainerAsync(string sessionId)
    {
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [ContainerName(sessionId)] = true }
            }
        });
        return containers.FirstOrDefault();
    }

    private async Task ApplyNetworkFilteringAsync(string sessionId)
    {
        var script = "iptables -F OUTPUT 2>/dev/null; ";

        // Always allow loopback and established connections
        script += "iptables -A OUTPUT -o lo -j ACCEPT; ";
        script += "iptables -A OUTPUT -m state --state ESTABLISHED,RELATED -j ACCEPT; ";
        // Allow DNS
        script += "iptables -A OUTPUT -p udp --dport 53 -j ACCEPT; ";
        script += "iptables -A OUTPUT -p tcp --dport 53 -j ACCEPT; ";

        if (!string.IsNullOrWhiteSpace(_config.NetworkWhitelist))
        {
            var hosts = _config.NetworkWhitelist.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var host in hosts)
            {
                // Resolve hostname and allow its IPs
                script += $"for ip in $(dig +short {host} 2>/dev/null || echo ''); do iptables -A OUTPUT -d $ip -j ACCEPT; done; ";
            }
            // Default deny after whitelist
            script += "iptables -A OUTPUT -j DROP; ";
        }

        if (!string.IsNullOrWhiteSpace(_config.NetworkBlacklist))
        {
            var hosts = _config.NetworkBlacklist.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var host in hosts)
            {
                script += $"for ip in $(dig +short {host} 2>/dev/null || echo ''); do iptables -A OUTPUT -d $ip -j DROP; done; ";
            }
        }

        await ExecAsync(sessionId, script, "/", 30);
    }
}
