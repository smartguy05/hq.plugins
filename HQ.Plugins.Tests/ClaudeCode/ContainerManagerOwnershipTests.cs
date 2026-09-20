using System.Threading;
using Docker.DotNet;
using Docker.DotNet.Models;
using HQ.Plugins.ClaudeCode;
using Moq;
using ServiceConfig = HQ.Plugins.ClaudeCode.Models.ServiceConfig;

namespace HQ.Plugins.Tests.ClaudeCode;

/// <summary>
/// WP6A-6 (adversarial re-review, 2026-09): the earlier fix only added a sessionId FORMAT check
/// (^[a-f0-9]{12}$ via GitArgValidation.IsValidSessionId). Every legitimately generated sessionId
/// already matches that exact format for every tenant, so a leaked, well-formed sessionId from
/// another org sailed straight through it — the review's cited repro is "obtain another org's
/// (well-formed, leaked) sessionId and call claude_code_get_diff / claude_code_continue /
/// claude_code_destroy_session", and no test exercised that case.
///
/// hq.plugins has no ITenantContext/OrgId anywhere (confirmed by grep — the same wall WP6A-5 was
/// deferred behind), so real OrgId-based binding isn't achievable from this repo alone. These
/// tests cover the strongest mitigation available without that plumbing: binding a session to a
/// fingerprint of the ServiceConfig that created it (see the <c>LabelOwner</c> remarks on
/// <see cref="ContainerManager"/> for exactly what this does and doesn't guarantee), verified here
/// against a MOCKED <see cref="IDockerClient"/> so the Docker-touching parts of the fix — which
/// have no live daemon in this test environment — are exercised for real rather than only their
/// pure helper functions.
/// </summary>
public class ContainerManagerOwnershipTests
{
    private const string SessionId = "abcdef012345"; // matches GitArgValidation.IsValidSessionId

    private static ServiceConfig ConfigA => new()
    {
        Name = "org-a-claude-code",
        Description = "Org A's Claude Code tool",
        AnthropicApiKey = "sk-ant-org-a-key",
        DockerImage = "hq-claude-code:latest",
        CloneBaseDir = "/workspace"
    };

    private static ServiceConfig ConfigB => new()
    {
        Name = "org-b-claude-code",
        Description = "Org B's Claude Code tool",
        AnthropicApiKey = "sk-ant-org-b-key",
        DockerImage = "hq-claude-code:latest",
        CloneBaseDir = "/workspace"
    };

    // ───────────────────────── Pure fingerprint/label logic ─────────────────────────

    [Fact]
    public void ComputeOwnerFingerprint_IsDeterministicForTheSameConfig()
    {
        Assert.Equal(ContainerManager.ComputeOwnerFingerprint(ConfigA), ContainerManager.ComputeOwnerFingerprint(ConfigA));
    }

    [Fact]
    public void ComputeOwnerFingerprint_DiffersAcrossDifferentConfigs()
    {
        // Two different agents' plugin configs are the closest proxy to tenant identity
        // available anywhere in hq.plugins (see the LabelOwner remarks in ContainerManager).
        Assert.NotEqual(ContainerManager.ComputeOwnerFingerprint(ConfigA), ContainerManager.ComputeOwnerFingerprint(ConfigB));
    }

    [Fact]
    public void IsOwnedBy_TrueWhenLabelMatches()
    {
        var fp = ContainerManager.ComputeOwnerFingerprint(ConfigA);
        var labels = new Dictionary<string, string> { ["hq.claudecode.owner"] = fp };

        Assert.True(ContainerManager.IsOwnedBy(labels, fp));
    }

    [Fact]
    public void IsOwnedBy_FalseWhenLabelBelongsToDifferentConfig()
    {
        // Direct unit-level proof of the actual repro: a container created under org A's config
        // is not "owned" from org B's perspective, even though the sessionId itself is perfectly
        // well-formed (that's covered end-to-end below and in ClaudeCodeServiceOwnershipTests).
        var ownerFingerprint = ContainerManager.ComputeOwnerFingerprint(ConfigA);
        var callerFingerprint = ContainerManager.ComputeOwnerFingerprint(ConfigB);
        var labels = new Dictionary<string, string> { ["hq.claudecode.owner"] = ownerFingerprint };

        Assert.False(ContainerManager.IsOwnedBy(labels, callerFingerprint));
    }

    [Fact]
    public void IsOwnedBy_FalseWhenLabelMissing()
    {
        // A pre-WP6A-6 container has no owner label at all — treat as foreign, never as trusted.
        var callerFingerprint = ContainerManager.ComputeOwnerFingerprint(ConfigA);
        Assert.False(ContainerManager.IsOwnedBy(new Dictionary<string, string>(), callerFingerprint));
    }

    // ───────────────────────── Against a mocked Docker daemon ─────────────────────────

    private static (Mock<IDockerClient> Client, Mock<IContainerOperations> Containers, Mock<IVolumeOperations> Volumes)
        MockClientWithExistingContainer(string ownerFingerprint)
    {
        var containerOps = new Mock<IContainerOperations>();
        containerOps
            .Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerListResponse>
            {
                new()
                {
                    ID = "victim-container-id",
                    State = "running",
                    Labels = new Dictionary<string, string> { ["hq.claudecode.owner"] = ownerFingerprint }
                }
            });

        var volumeOps = new Mock<IVolumeOperations>();

        var client = new Mock<IDockerClient>();
        client.SetupGet(c => c.Containers).Returns(containerOps.Object);
        client.SetupGet(c => c.Volumes).Returns(volumeOps.Object);

        return (client, containerOps, volumeOps);
    }

    [Fact]
    public async Task IsSessionAccessibleAsync_ReturnsFalse_ForContainerOwnedByDifferentConfig()
    {
        var ownerFingerprint = ContainerManager.ComputeOwnerFingerprint(ConfigA);
        var (client, _, _) = MockClientWithExistingContainer(ownerFingerprint);

        var containers = new ContainerManager(ConfigB, client.Object);

        Assert.False(await containers.IsSessionAccessibleAsync(SessionId));
    }

    [Fact]
    public async Task IsSessionAccessibleAsync_ReturnsTrue_ForContainerOwnedBySameConfig()
    {
        var ownerFingerprint = ContainerManager.ComputeOwnerFingerprint(ConfigA);
        var (client, _, _) = MockClientWithExistingContainer(ownerFingerprint);

        var containers = new ContainerManager(ConfigA, client.Object);

        Assert.True(await containers.IsSessionAccessibleAsync(SessionId));
    }

    [Fact]
    public async Task IsSessionAccessibleAsync_ReturnsTrue_WhenNoContainerExistsYet()
    {
        var containerOps = new Mock<IContainerOperations>();
        containerOps
            .Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerListResponse>());

        var client = new Mock<IDockerClient>();
        client.SetupGet(c => c.Containers).Returns(containerOps.Object);

        var containers = new ContainerManager(ConfigA, client.Object);

        Assert.True(await containers.IsSessionAccessibleAsync(SessionId));
    }

    [Fact]
    public async Task DestroyContainerAsync_ThrowsAndNeverTouchesDocker_ForContainerOwnedByDifferentConfig()
    {
        var ownerFingerprint = ContainerManager.ComputeOwnerFingerprint(ConfigA);
        var (client, containerOps, volumeOps) = MockClientWithExistingContainer(ownerFingerprint);

        var containers = new ContainerManager(ConfigB, client.Object);

        // WP6A-6 repro: claude_code_destroy_session against another org's leaked, well-formed
        // sessionId used to unconditionally stop/remove the container and delete its data
        // volume — irreversibly. It must now be refused before any of those calls are made.
        await Assert.ThrowsAsync<InvalidOperationException>(() => containers.DestroyContainerAsync(SessionId));

        containerOps.Verify(c => c.StopContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()), Times.Never);
        containerOps.Verify(c => c.RemoveContainerAsync(It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Never);
        volumeOps.Verify(v => v.RemoveAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DestroyContainerAsync_Succeeds_ForContainerOwnedBySameConfig()
    {
        var ownerFingerprint = ContainerManager.ComputeOwnerFingerprint(ConfigA);
        var (client, containerOps, volumeOps) = MockClientWithExistingContainer(ownerFingerprint);

        var containers = new ContainerManager(ConfigA, client.Object);

        var ex = await Record.ExceptionAsync(() => containers.DestroyContainerAsync(SessionId));

        Assert.Null(ex);
        containerOps.Verify(c => c.StopContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()), Times.Once);
        containerOps.Verify(c => c.RemoveContainerAsync(It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Once);
        volumeOps.Verify(v => v.RemoveAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetContainerStatusAsync_ReturnsNotFound_ForContainerOwnedByDifferentConfig()
    {
        var ownerFingerprint = ContainerManager.ComputeOwnerFingerprint(ConfigA);
        var (client, _, _) = MockClientWithExistingContainer(ownerFingerprint);

        var containers = new ContainerManager(ConfigB, client.Object);

        // Must look identical to "doesn't exist" — a distinct status would confirm to an
        // attacker that a session with this id exists under some other org.
        Assert.Equal("not_found", await containers.GetContainerStatusAsync(SessionId));
    }

    [Fact]
    public async Task EnsureContainerAsync_Throws_WhenReusingSessionIdOwnedByDifferentConfig()
    {
        var ownerFingerprint = ContainerManager.ComputeOwnerFingerprint(ConfigA);
        var (client, containerOps, _) = MockClientWithExistingContainer(ownerFingerprint);

        var containers = new ContainerManager(ConfigB, client.Object);

        // RunTask's resume-by-id path calls this directly. Reusing/starting another config's
        // container here would hand a foreign session's checkout and repo access to whoever
        // supplied its leaked sessionId.
        await Assert.ThrowsAsync<InvalidOperationException>(() => containers.EnsureContainerAsync(SessionId));

        containerOps.Verify(c => c.StartContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()), Times.Never);
        containerOps.Verify(c => c.CreateContainerAsync(It.IsAny<CreateContainerParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
