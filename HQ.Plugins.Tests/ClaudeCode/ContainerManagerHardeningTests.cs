using HQ.Plugins.ClaudeCode;
using HQ.Plugins.ClaudeCode.Models;

namespace HQ.Plugins.Tests.ClaudeCode;

/// <summary>
/// WP6A-3 (2026-09 security review, cluster P7): the sandbox container was created with
/// NetworkMode "bridge" (the host-wide default network) and no CapDrop, ReadonlyRootfs or
/// SecurityOpt at all — "runs as root with no cap_drop, no read-only rootfs and bridge
/// networking". <see cref="ContainerManager.BuildCreateContainerParameters"/> is the pure,
/// Docker-daemon-free function that builds those parameters, so the hardening can be asserted
/// directly without a live daemon (which is unavailable in this test environment).
/// </summary>
public class ContainerManagerHardeningTests
{
    private static ServiceConfig CreateConfig() => new()
    {
        Name = "claude-code",
        DockerImage = "hq-claude-code:latest",
        CloneBaseDir = "/workspace",
        MemoryLimitMb = 512,
        CpuShares = 1024,
        PidsLimit = 100
    };

    [Fact]
    public void BuildCreateContainerParameters_DropsAllCapabilities()
    {
        var parms = ContainerManager.BuildCreateContainerParameters(
            CreateConfig(), "hq-claude-code-abc123", "hq-claude-code-abc123-data",
            new Dictionary<string, string>(), new List<string>(), needsNetworkAdmin: false);

        Assert.NotNull(parms.HostConfig.CapDrop);
        Assert.Contains("ALL", parms.HostConfig.CapDrop);
    }

    [Fact]
    public void BuildCreateContainerParameters_SetsReadonlyRootfs()
    {
        var parms = ContainerManager.BuildCreateContainerParameters(
            CreateConfig(), "hq-claude-code-abc123", "hq-claude-code-abc123-data",
            new Dictionary<string, string>(), new List<string>(), needsNetworkAdmin: false);

        Assert.True(parms.HostConfig.ReadonlyRootfs);
    }

    [Fact]
    public void BuildCreateContainerParameters_SetsNoNewPrivileges()
    {
        var parms = ContainerManager.BuildCreateContainerParameters(
            CreateConfig(), "hq-claude-code-abc123", "hq-claude-code-abc123-data",
            new Dictionary<string, string>(), new List<string>(), needsNetworkAdmin: false);

        Assert.NotNull(parms.HostConfig.SecurityOpt);
        Assert.Contains("no-new-privileges=true", parms.HostConfig.SecurityOpt);
    }

    [Fact]
    public void BuildCreateContainerParameters_UsesDedicatedNetworkNotSharedDefaultBridge()
    {
        var parms = ContainerManager.BuildCreateContainerParameters(
            CreateConfig(), "hq-claude-code-abc123", "hq-claude-code-abc123-data",
            new Dictionary<string, string>(), new List<string>(), needsNetworkAdmin: false);

        Assert.Equal(ContainerManager.SandboxNetworkName, parms.HostConfig.NetworkMode);
        Assert.NotEqual("bridge", parms.HostConfig.NetworkMode);
    }

    [Fact]
    public void BuildCreateContainerParameters_StillAddsNetAdminWhenFilteringConfigured()
    {
        // ReadonlyRootfs/CapDrop must not regress the existing NET_ADMIN-for-iptables path.
        var parms = ContainerManager.BuildCreateContainerParameters(
            CreateConfig(), "hq-claude-code-abc123", "hq-claude-code-abc123-data",
            new Dictionary<string, string>(), new List<string>(), needsNetworkAdmin: true);

        Assert.NotNull(parms.HostConfig.CapAdd);
        Assert.Contains("NET_ADMIN", parms.HostConfig.CapAdd);
    }

    [Fact]
    public void BuildCreateContainerParameters_ProvidesWritableTmpfsForRootAndTmp()
    {
        // ReadonlyRootfs=true would otherwise break npm/git/Claude Code config writes under
        // $HOME (root) and /tmp.
        var parms = ContainerManager.BuildCreateContainerParameters(
            CreateConfig(), "hq-claude-code-abc123", "hq-claude-code-abc123-data",
            new Dictionary<string, string>(), new List<string>(), needsNetworkAdmin: false);

        Assert.NotNull(parms.HostConfig.Tmpfs);
        Assert.True(parms.HostConfig.Tmpfs.ContainsKey("/tmp"));
        Assert.True(parms.HostConfig.Tmpfs.ContainsKey("/root"));
    }
}
