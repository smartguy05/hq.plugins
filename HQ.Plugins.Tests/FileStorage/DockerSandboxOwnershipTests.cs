using System.Threading;
using Docker.DotNet;
using Docker.DotNet.Models;
using HQ.Plugins.FileStorage;
using Moq;
using ServiceConfig = HQ.Plugins.FileStorage.Models.ServiceConfig;

namespace HQ.Plugins.Tests.FileStorage;

/// <summary>
/// WP6A-5 (minor, adversarial re-review, 2026-09, cluster P7): "DockerSandbox was not
/// refactored to take an injectable IDockerClient (unlike ContainerManager, which was
/// refactored specifically so ContainerManagerOwnershipTests could exercise the ownership gate
/// against a mocked Docker client). As a result, EnsureWorkspaceOwnedByOrgAsync — the method
/// that actually denies cross-org access — is exercised by zero tests."
///
/// This file closes that gap the same way ContainerManagerOwnershipTests/
/// ClaudeCodeServiceOwnershipTests do for ClaudeCode: against a MOCKED <see cref="IDockerClient"/>
/// so the Docker-touching ownership gate is exercised for real, not just its pure naming helpers.
/// </summary>
public class DockerSandboxOwnershipTests
{
    private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrgB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string WorkspaceId = "ws-shared-id";

    private static ServiceConfig Config => new() { DefaultImage = "hq-workspace:latest" };

    /// <summary>Builds a mocked Docker daemon that reports a single container — the exact name
    /// <see cref="DockerSandbox.ContainerName"/> would compute for (<paramref name="owningOrg"/>,
    /// <see cref="WorkspaceId"/>) — carrying the <c>hq.org.id</c> label for
    /// <paramref name="owningOrg"/>.</summary>
    private static (Mock<IDockerClient> Client, Mock<IContainerOperations> Containers, Mock<IVolumeOperations> Volumes, Mock<IExecOperations> Exec)
        MockClientWithExistingWorkspace(Guid owningOrg)
    {
        var containerName = DockerSandbox.ContainerName(owningOrg, WorkspaceId);

        var containerOps = new Mock<IContainerOperations>();
        containerOps
            .Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerListResponse>
            {
                new()
                {
                    ID = "victim-container-id",
                    State = "running",
                    Names = new List<string> { "/" + containerName },
                    Labels = new Dictionary<string, string> { [DockerSandbox.LabelOrgId] = owningOrg.ToString() }
                }
            });
        containerOps
            .Setup(c => c.InspectContainerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerInspectResponse
            {
                State = new ContainerState { Status = "running", Running = true },
                Config = new Config(),
                Mounts = new List<MountPoint>()
            });

        var volumeOps = new Mock<IVolumeOperations>();
        var execOps = new Mock<IExecOperations>();

        var client = new Mock<IDockerClient>();
        client.SetupGet(c => c.Containers).Returns(containerOps.Object);
        client.SetupGet(c => c.Volumes).Returns(volumeOps.Object);
        client.SetupGet(c => c.Exec).Returns(execOps.Object);

        return (client, containerOps, volumeOps, execOps);
    }

    [Fact]
    public async Task DestroyContainerAsync_ThrowsAndNeverTouchesDocker_ForContainerOwnedByDifferentConfig()
    {
        var (client, containerOps, volumeOps, _) = MockClientWithExistingWorkspace(OrgA);
        var sandbox = new DockerSandbox(Config, client.Object);

        // The review's repro: org B calls workspace_destroy against org A's workspace id. It
        // must be refused as "not found" before any destructive Docker call is made.
        await Assert.ThrowsAsync<InvalidOperationException>(() => sandbox.DestroyWorkspaceAsync(OrgB, WorkspaceId));

        containerOps.Verify(c => c.StopContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()), Times.Never);
        containerOps.Verify(c => c.RemoveContainerAsync(It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Never);
        volumeOps.Verify(v => v.RemoveAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DestroyContainerAsync_Succeeds_ForContainerOwnedBySameConfig()
    {
        var (client, containerOps, volumeOps, _) = MockClientWithExistingWorkspace(OrgA);
        var sandbox = new DockerSandbox(Config, client.Object);

        var ex = await Record.ExceptionAsync(() => sandbox.DestroyWorkspaceAsync(OrgA, WorkspaceId));

        Assert.Null(ex);
        containerOps.Verify(c => c.RemoveContainerAsync(It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Once);
        volumeOps.Verify(v => v.RemoveAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetContainerStatusAsync_ReturnsNotFound_ForContainerOwnedByDifferentConfig()
    {
        var (client, _, _, _) = MockClientWithExistingWorkspace(OrgA);
        var sandbox = new DockerSandbox(Config, client.Object);

        // workspace_status against another org's workspace id must look identical to "doesn't
        // exist" — a distinct error would confirm to a probing caller that the id exists under
        // some other tenant.
        var ex = await Record.ExceptionAsync(() => sandbox.GetStatusAsync(OrgB, WorkspaceId));

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public async Task GetContainerStatusAsync_Succeeds_ForContainerOwnedBySameConfig()
    {
        var (client, _, _, _) = MockClientWithExistingWorkspace(OrgA);
        var sandbox = new DockerSandbox(Config, client.Object);

        var ex = await Record.ExceptionAsync(() => sandbox.GetStatusAsync(OrgA, WorkspaceId));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ExecAsync_ThrowsAndNeverExecs_ForContainerOwnedByDifferentConfig()
    {
        var (client, _, _, execOps) = MockClientWithExistingWorkspace(OrgA);
        var sandbox = new DockerSandbox(Config, client.Object);

        // The review's repro: workspace_exec against another org's leaked workspace id must
        // never run a single command inside that container.
        await Assert.ThrowsAsync<InvalidOperationException>(() => sandbox.ExecAsync(OrgB, WorkspaceId, "echo hi", "/", 5));

        execOps.Verify(e => e.ExecCreateContainerAsync(It.IsAny<string>(), It.IsAny<ContainerExecCreateParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListWorkspacesAsync_UsesOrgScopedServerSideFilter_AgainstAMockedDaemon()
    {
        // End-to-end proof (not just the pure BuildOrgScopedListFilters unit test) that
        // ListWorkspacesAsync actually passes the org-scoped filter to Docker, so org B's
        // workspace_list call is server-side filtered and never receives org A's containers.
        var containerOps = new Mock<IContainerOperations>();
        ContainersListParameters capturedParams = null;
        containerOps
            .Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .Callback<ContainersListParameters, CancellationToken>((p, _) => capturedParams = p)
            .ReturnsAsync(new List<ContainerListResponse>());

        var client = new Mock<IDockerClient>();
        client.SetupGet(c => c.Containers).Returns(containerOps.Object);

        var sandbox = new DockerSandbox(Config, client.Object);
        await sandbox.ListWorkspacesAsync(OrgB);

        Assert.NotNull(capturedParams);
        Assert.True(capturedParams.Filters["label"][$"hq.org.id={OrgB}"]);
        Assert.DoesNotContain($"hq.org.id={OrgA}", capturedParams.Filters["label"].Keys);
    }
}
