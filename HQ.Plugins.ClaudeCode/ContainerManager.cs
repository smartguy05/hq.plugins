using Docker.DotNet;
using Docker.DotNet.Models;
using ServiceConfig = HQ.Plugins.ClaudeCode.Models.ServiceConfig;

namespace HQ.Plugins.ClaudeCode;

internal class ContainerManager
{
    private readonly DockerClient _client;
    private readonly ServiceConfig _config;

    private const string LabelPlugin = "hq.plugin";
    private const string LabelSessionId = "hq.claudecode.session";
    private const string LabelCreated = "hq.created";

    public ContainerManager(ServiceConfig config)
    {
        _config = config;

        if (!string.IsNullOrWhiteSpace(config.DockerHost))
        {
            _client = new DockerClientConfiguration(new Uri(config.DockerHost)).CreateClient();
        }
        else
        {
            var uri = OperatingSystem.IsWindows()
                ? new Uri("npipe://./pipe/docker_engine")
                : new Uri("unix:///var/run/docker.sock");
            _client = new DockerClientConfiguration(uri).CreateClient();
        }
    }

    private static string ContainerName(string sessionId) => $"hq-claude-code-{sessionId}";
    private static string VolumeName(string sessionId) => $"hq-claude-code-{sessionId}-data";

    public async Task<string> EnsureContainerAsync(string sessionId)
    {
        var containerName = ContainerName(sessionId);
        var existing = await FindContainerAsync(sessionId);

        if (existing != null)
        {
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
            [LabelCreated] = DateTime.UtcNow.ToString("O")
        };

        var memoryBytes = _config.MemoryLimitMb * 1024 * 1024;

        // Determine if we need NET_ADMIN for network filtering
        var capAdd = new List<string>();
        var hasNetworkFiltering = !string.IsNullOrWhiteSpace(_config.NetworkWhitelist) ||
                                  !string.IsNullOrWhiteSpace(_config.NetworkBlacklist);
        if (hasNetworkFiltering)
            capAdd.Add("NET_ADMIN");

        var createParams = new CreateContainerParameters
        {
            Image = _config.DockerImage,
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
                        Target = _config.CloneBaseDir
                    }
                },
                NetworkMode = "bridge",
                Memory = memoryBytes,
                MemorySwap = memoryBytes,
                PidsLimit = _config.PidsLimit,
                CPUShares = _config.CpuShares,
                CapAdd = capAdd.Count > 0 ? capAdd : null,
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No }
            }
        };

        var response = await _client.Containers.CreateContainerAsync(createParams);
        await _client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters());

        // Apply network filtering if configured
        if (hasNetworkFiltering)
            await ApplyNetworkFilteringAsync(sessionId);

        return response.ID;
    }

    public async Task DestroyContainerAsync(string sessionId)
    {
        var containerName = ContainerName(sessionId);

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

    public async Task<(string Stdout, string Stderr, long ExitCode)> ExecAsync(
        string sessionId, string command, string workingDir, int timeoutSeconds)
    {
        var containerName = ContainerName(sessionId);

        var execParams = new ContainerExecCreateParameters
        {
            Cmd = new List<string> { "/bin/bash", "-c", command },
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
        return existing?.State ?? "not_found";
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
