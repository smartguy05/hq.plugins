using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.FileStorage.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Docker/Podman socket URL, e.g. unix:///var/run/docker.sock or npipe://./pipe/docker_engine")]
    public string DockerHost { get; set; }

    [Tooltip("Container image to use for workspace containers")]
    public string DefaultImage { get; set; } = "hq-workspace:latest";

    [Tooltip("Maximum memory each workspace container can use, in MB")]
    public long MemoryLimitMb { get; set; } = 512;

    [Tooltip("CPU shares for workspace containers. 1024 = 1 full core equivalent.")]
    public long CpuShares { get; set; } = 1024;

    [Tooltip("Maximum number of processes allowed in the container")]
    public long PidsLimit { get; set; } = 100;

    [Tooltip("Maximum disk space for the workspace volume, in MB")]
    public long WorkspaceSizeMb { get; set; } = 256;
}
