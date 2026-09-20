using System.Text.Json;
using System.Threading;
using Docker.DotNet;
using Docker.DotNet.Models;
using HQ.Plugins.ClaudeCode;
using HQ.Plugins.ClaudeCode.Models;
using Moq;
using ServiceConfig = HQ.Plugins.ClaudeCode.Models.ServiceConfig;

namespace HQ.Plugins.Tests.ClaudeCode;

/// <summary>
/// WP6A-6 (adversarial re-review, 2026-09): "No test in ClaudeCodeServiceSessionValidationTests
/// exercises 'valid-format sessionId belonging to another tenant' — every test only supplies
/// malformed/injection-shaped ids, so the test suite cannot and does not catch this gap." This
/// file is exactly that missing test: a well-formed sessionId (passes GitArgValidation.
/// IsValidSessionId) whose container belongs to a different agent's ServiceConfig, run through
/// the real public <see cref="ClaudeCodeService"/> tool methods the review's repro named —
/// claude_code_get_diff / claude_code_continue / claude_code_destroy_session — against a mocked
/// <see cref="IDockerClient"/> so the assertion is "the container is never touched", not just
/// "some validation function returns false".
/// </summary>
public class ClaudeCodeServiceOwnershipTests
{
    private const string SessionId = "abcdef012345"; // matches GitArgValidation.IsValidSessionId

    private static ServiceConfig VictimConfig => new()
    {
        Name = "victim-org-claude-code",
        AnthropicApiKey = "sk-ant-victim-key",
        CloneBaseDir = "/workspace"
    };

    private static ServiceConfig AttackerConfig => new()
    {
        Name = "attacker-org-claude-code",
        AnthropicApiKey = "sk-ant-attacker-key",
        CloneBaseDir = "/workspace"
    };

    private static JsonDocument AsJson(object result) =>
        JsonDocument.Parse(JsonSerializer.Serialize(result));

    /// <summary>Builds a <see cref="ClaudeCodeService"/> whose Docker daemon reports a single
    /// container, well-formed and running, but owned by <see cref="VictimConfig"/> — while the
    /// service itself is configured as <see cref="AttackerConfig"/>. This is the review's repro:
    /// "obtain another org's (well-formed, leaked) sessionId".</summary>
    private static (ClaudeCodeService Service, Mock<IExecOperations> Exec, Mock<IContainerOperations> Containers, Mock<IVolumeOperations> Volumes)
        CreateServiceHoldingForeignSession()
    {
        var victimOwnerFingerprint = ContainerManager.ComputeOwnerFingerprint(VictimConfig);

        var containerOps = new Mock<IContainerOperations>();
        containerOps
            .Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerListResponse>
            {
                new()
                {
                    ID = "victim-container-id",
                    State = "running",
                    Labels = new Dictionary<string, string> { ["hq.claudecode.owner"] = victimOwnerFingerprint }
                }
            });

        var execOps = new Mock<IExecOperations>();
        var volumeOps = new Mock<IVolumeOperations>();

        var client = new Mock<IDockerClient>();
        client.SetupGet(c => c.Containers).Returns(containerOps.Object);
        client.SetupGet(c => c.Exec).Returns(execOps.Object);
        client.SetupGet(c => c.Volumes).Returns(volumeOps.Object);

        var containerManager = new ContainerManager(AttackerConfig, client.Object);
        var service = new ClaudeCodeService(AttackerConfig, (_, _, _) => Task.CompletedTask, containerManager);

        return (service, execOps, containerOps, volumeOps);
    }

    [Fact]
    public async Task GetDiff_RejectsWellFormedSessionIdBelongingToAnotherConfig()
    {
        var (service, execOps, _, _) = CreateServiceHoldingForeignSession();

        var result = await service.GetDiff(new ServiceConfig(), new GetDiffArgs { SessionId = SessionId });

        using var doc = AsJson(result);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());

        // The actual bug: previously this would exec `git diff HEAD` straight into the victim's
        // container and return its output. It must never reach Exec at all now.
        execOps.Verify(e => e.ExecCreateContainerAsync(It.IsAny<string>(), It.IsAny<ContainerExecCreateParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ContinueSession_RejectsWellFormedSessionIdBelongingToAnotherConfig()
    {
        var (service, execOps, _, _) = CreateServiceHoldingForeignSession();

        var result = await service.ContinueSession(
            new ServiceConfig(), new ContinueArgs { SessionId = SessionId, Prompt = "do something else" });

        using var doc = AsJson(result);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
        execOps.Verify(e => e.ExecCreateContainerAsync(It.IsAny<string>(), It.IsAny<ContainerExecCreateParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DestroySession_RejectsWellFormedSessionIdBelongingToAnotherConfig()
    {
        var (service, _, containerOps, volumeOps) = CreateServiceHoldingForeignSession();

        var result = await service.DestroySession(new ServiceConfig(), new DestroySessionArgs { SessionId = SessionId });

        using var doc = AsJson(result);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());

        // The actual bug: previously this would irreversibly stop/remove the victim's container
        // and delete its data volume.
        containerOps.Verify(c => c.RemoveContainerAsync(It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Never);
        volumeOps.Verify(v => v.RemoveAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatus_RejectsWellFormedSessionIdBelongingToAnotherConfig()
    {
        var (service, _, _, _) = CreateServiceHoldingForeignSession();

        var result = await service.GetStatus(new ServiceConfig(), new StatusArgs { SessionId = SessionId });

        using var doc = AsJson(result);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
    }

    [Fact]
    public async Task GetDiff_SucceedsPastTheGate_WhenSessionBelongsToTheSameConfig()
    {
        // Control case: the gate must not block a caller's OWN session. Uses a container list
        // owned by AttackerConfig itself, so IsSessionAccessibleAsync returns true and the call
        // proceeds to Exec (which is mocked to fail fast rather than hang).
        var ownerFingerprint = ContainerManager.ComputeOwnerFingerprint(AttackerConfig);

        var containerOps = new Mock<IContainerOperations>();
        containerOps
            .Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerListResponse>
            {
                new()
                {
                    ID = "own-container-id",
                    State = "running",
                    Labels = new Dictionary<string, string> { ["hq.claudecode.owner"] = ownerFingerprint }
                }
            });

        var execOps = new Mock<IExecOperations>();
        execOps
            .Setup(e => e.ExecCreateContainerAsync(It.IsAny<string>(), It.IsAny<ContainerExecCreateParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("exec reached — gate correctly let an owned session through"));

        var client = new Mock<IDockerClient>();
        client.SetupGet(c => c.Containers).Returns(containerOps.Object);
        client.SetupGet(c => c.Exec).Returns(execOps.Object);

        var containerManager = new ContainerManager(AttackerConfig, client.Object);
        var service = new ClaudeCodeService(AttackerConfig, (_, _, _) => Task.CompletedTask, containerManager);

        var ex = await Record.ExceptionAsync(() => service.GetDiff(new ServiceConfig(), new GetDiffArgs { SessionId = SessionId }));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("gate correctly let an owned session through", ex.Message);
    }
}
