using HQ.Models.Interfaces;

namespace HQ.Plugins.FileStorage.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string DockerHost { get; set; }
    public string DefaultImage { get; set; } = "hq-workspace:latest";
    public long MemoryLimitMb { get; set; } = 512;
    public long CpuShares { get; set; } = 1024;
    public long PidsLimit { get; set; } = 100;
    public long WorkspaceSizeMb { get; set; } = 256;
}
