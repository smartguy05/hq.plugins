using HQ.Models.Enums;
using HQ.Plugins.FileStorage;
using HQ.Plugins.FileStorage.Models;

namespace HQ.Plugins.Tests.FileStorage;

/// <summary>
/// WP6A-5 / WP6A-11 (2026-09 security review, cluster P7).
///
/// WP6A-5 (High) — cross-tenant workspace access via an unvalidated, unscoped global
/// workspace/team namespace. FULL FIX (cluster P7, second pass): every workspace/team-scoped
/// tool now carries a host-injected <c>OrganizationId</c> (HQ.Services.Plugin.PluginService.
/// InjectOrganizationId force-overwrites this on every tool call before the plugin sees it —
/// see HQ.Plugins.Tasks.Models.ToolArgs for the repo's existing convention this follows).
/// Container/volume names are namespaced by a hash of that org id
/// (<see cref="DockerSandbox.OrgTag"/>), an <c>hq.org.id</c> label is stamped at creation and
/// verified on every read/write/exec/list/status/destroy, workspace_list is filtered
/// server-side by that label, and a null/empty organization id is refused outright (fail
/// closed — never falls back to a global/unscoped namespace).
///
/// WP6A-11 (this cluster's scope only): exec commands are attacker/agent-controlled text that
/// was logged verbatim into the shared log sink; it is now truncated first.
/// </summary>
public class FileStorageServiceSecurityTests
{
    private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrgB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// This dev host actually has a reachable Docker daemon with a real "hq-workspace:latest"
    /// image (unlike the assumption written into the previous pass of this test file) — a test
    /// here that clears validation and falls through to DockerSandbox would otherwise create a
    /// REAL container/volume against whatever daemon happens to be reachable at
    /// unix:///var/run/docker.sock, alongside the actual HQ deployment's own workspaces. Point
    /// every test at a guaranteed-nonexistent socket path instead, so "fails for an unrelated
    /// Docker reason" is a fast, deterministic, side-effect-free connection failure regardless
    /// of what happens to be running on the machine executing the suite.
    /// </summary>
    private static ServiceConfig UnreachableDockerConfig() => new()
    {
        DockerHost = "unix:///nonexistent/hq-test-only.sock"
    };

    private static FileStorageService CreateService() =>
        new(UnreachableDockerConfig(), (_, _, _) => Task.CompletedTask);

    // ───────────────────────────── WP6A-5: null org id is refused (fail closed) ─────────────────────────────

    [Fact]
    public async Task CreateWorkspace_NullOrganizationId_IsRefused()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.CreateWorkspace(
            new ServiceConfig(),
            new CreateWorkspaceArgs { OrganizationId = null, WorkspaceId = "ws-abc123" }));

        Assert.IsType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task CreateWorkspace_EmptyGuidOrganizationId_IsRefused()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.CreateWorkspace(
            new ServiceConfig(),
            new CreateWorkspaceArgs { OrganizationId = Guid.Empty, WorkspaceId = "ws-abc123" }));

        Assert.IsType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task DestroyWorkspace_NullOrganizationId_IsRefused()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.DestroyWorkspace(
            new ServiceConfig(),
            new DestroyWorkspaceArgs { OrganizationId = null, WorkspaceId = "ws-abc123" }));

        Assert.IsType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task ListWorkspaces_NullOrganizationId_IsRefused()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.ListWorkspaces(
            new ServiceConfig(),
            new ListWorkspacesArgs { OrganizationId = null }));

        Assert.IsType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task GetWorkspaceStatus_NullOrganizationId_IsRefused()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.GetWorkspaceStatus(
            new ServiceConfig(),
            new WorkspaceStatusArgs { OrganizationId = null, WorkspaceId = "ws-abc123" }));

        Assert.IsType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task ReadFile_NullOrganizationId_IsRefused()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.ReadFile(
            new ServiceConfig(),
            new ReadFileArgs { OrganizationId = null, WorkspaceId = "ws-abc123", FilePath = "/workspace/x" }));

        Assert.IsType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task WriteFile_NullOrganizationId_IsRefused()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.WriteFile(
            new ServiceConfig(),
            new WriteFileArgs { OrganizationId = null, WorkspaceId = "ws-abc123", FilePath = "/workspace/x", FileContent = "hi" }));

        Assert.IsType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task ListFiles_NullOrganizationId_IsRefused()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.ListFiles(
            new ServiceConfig(),
            new ListFilesArgs { OrganizationId = null, WorkspaceId = "ws-abc123" }));

        Assert.IsType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task DeleteFile_NullOrganizationId_IsRefused()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.DeleteFile(
            new ServiceConfig(),
            new DeleteFileArgs { OrganizationId = null, WorkspaceId = "ws-abc123", FilePath = "/workspace/x" }));

        Assert.IsType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task ExecCommand_NullOrganizationId_IsRefused()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.ExecCommand(
            new ServiceConfig(),
            new ExecCommandArgs { OrganizationId = null, WorkspaceId = "ws-abc123", Command = "echo hi" }));

        Assert.IsType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task ExecScript_NullOrganizationId_IsRefused()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.ExecScript(
            new ServiceConfig(),
            new ExecScriptArgs { OrganizationId = null, WorkspaceId = "ws-abc123", ScriptContent = "print(1)", ScriptType = "python" }));

        Assert.IsType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task CopyBetweenWorkspaces_NullOrganizationId_IsRefused()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.CopyBetweenWorkspaces(
            new ServiceConfig(),
            new CopyBetweenWorkspacesArgs
            {
                OrganizationId = null,
                SourceWorkspaceId = "ws-a",
                SourcePath = "/workspace/x",
                DestWorkspaceId = "ws-b",
                DestPath = "/workspace/y"
            }));

        Assert.IsType<UnauthorizedAccessException>(ex);
    }

    // ───────────────────────────── WP6A-5: a real org id clears validation ─────────────────────────────

    [Fact]
    public async Task CreateWorkspace_WithOrganizationId_DoesNotRejectOnOrgGrounds()
    {
        var service = CreateService();

        // A real org id must clear the org check and only fail later, for an unrelated reason
        // (no Docker daemon in this test environment) — never UnauthorizedAccessException.
        var ex = await Record.ExceptionAsync(() => service.CreateWorkspace(
            new ServiceConfig(),
            new CreateWorkspaceArgs { OrganizationId = OrgA, WorkspaceId = "ws-abc123" }));

        Assert.NotNull(ex);
        Assert.IsNotType<UnauthorizedAccessException>(ex);
    }

    [Theory]
    [InlineData("team; rm -rf /")]
    [InlineData("../../etc/passwd")]
    [InlineData("team$(whoami)")]
    [InlineData("-x")]
    public async Task CreateWorkspace_RejectsMalformedTeamId(string teamId)
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.CreateWorkspace(
            new ServiceConfig(),
            new CreateWorkspaceArgs { OrganizationId = OrgA, WorkspaceId = "ws-abc123", TeamId = teamId }));

        Assert.IsType<ArgumentException>(ex);
    }

    [Fact]
    public async Task CreateWorkspace_DoesNotRejectWellFormedTeamIdOnFormatGrounds()
    {
        var service = CreateService();

        // A well-formed teamId must clear validation and only fail later, for an unrelated
        // reason (no Docker daemon in this test environment) — never ArgumentException.
        var ex = await Record.ExceptionAsync(() => service.CreateWorkspace(
            new ServiceConfig(),
            new CreateWorkspaceArgs { OrganizationId = OrgA, WorkspaceId = "ws-abc123", TeamId = "team-42" }));

        Assert.NotNull(ex);
        Assert.IsNotType<ArgumentException>(ex);
    }

    [Fact]
    public async Task ExecCommand_LogsTruncatedCommandRatherThanFullAttackerPayload()
    {
        string capturedMessage = null;
        var service = new FileStorageService(new ServiceConfig(),
            (_, message, _) =>
            {
                capturedMessage ??= message;
                return Task.CompletedTask;
            });

        var hugeCommand = "echo " + new string('A', 5000);

        // The Docker call after logging will fail (no daemon) — that's expected and irrelevant
        // to this test, which only cares what reached the log sink before that point.
        await Record.ExceptionAsync(() => service.ExecCommand(
            new ServiceConfig(),
            new ExecCommandArgs { OrganizationId = OrgA, WorkspaceId = "ws-abc123", Command = hugeCommand }));

        Assert.NotNull(capturedMessage);
        Assert.DoesNotContain(hugeCommand, capturedMessage);
        Assert.Contains("truncated", capturedMessage);
    }

    // ───────────────────────────── WP6A-5: cross-org isolation at the naming layer ─────────────────────────────
    //
    // No Docker daemon is available in this test environment (consistent with the rest of this
    // file), so end-to-end "org A creates a workspace, org B cannot see/read/destroy it" is an
    // integration-level concern. What IS fully unit-testable — and is the actual mechanism that
    // makes cross-org access impossible — is that the container/volume naming and the
    // workspace_list filter are pure, deterministic functions of the organization id, and that
    // two different orgs NEVER produce the same Docker resource name/filter for the same
    // caller-supplied workspaceId/teamId.

    [Fact]
    public void ContainerName_DifferentOrgsSameWorkspaceId_ProduceDifferentContainerNames()
    {
        var nameA = DockerSandbox.ContainerName(OrgA, "ws-shared-id");
        var nameB = DockerSandbox.ContainerName(OrgB, "ws-shared-id");

        Assert.NotEqual(nameA, nameB);
    }

    [Fact]
    public void VolumeName_DifferentOrgsSameWorkspaceId_ProduceDifferentVolumeNames()
    {
        var volA = DockerSandbox.VolumeName(OrgA, "ws-shared-id");
        var volB = DockerSandbox.VolumeName(OrgB, "ws-shared-id");

        Assert.NotEqual(volA, volB);
    }

    [Fact]
    public void TeamVolumeName_DifferentOrgsSameTeamId_ProduceDifferentVolumeNames()
    {
        var volA = DockerSandbox.TeamVolumeName(OrgA, "team-shared-id");
        var volB = DockerSandbox.TeamVolumeName(OrgB, "team-shared-id");

        Assert.NotEqual(volA, volB);
    }

    [Fact]
    public void ContainerName_SameOrgAndWorkspaceId_IsDeterministic()
    {
        // Same org sees its own workspace: repeated calls for the same (org, workspaceId) must
        // resolve to the exact same container every time.
        var first = DockerSandbox.ContainerName(OrgA, "ws-abc123");
        var second = DockerSandbox.ContainerName(OrgA, "ws-abc123");

        Assert.Equal(first, second);
    }

    [Fact]
    public void OrgTag_ProducesDockerNameSafeCharactersOnly()
    {
        var tag = DockerSandbox.OrgTag(OrgA);

        Assert.Matches("^[a-z0-9]{12}$", tag);
    }

    [Fact]
    public void BuildOrgScopedListFilters_ScopesToTheGivenOrgOnly()
    {
        var filtersA = DockerSandbox.BuildOrgScopedListFilters(OrgA);
        var filtersB = DockerSandbox.BuildOrgScopedListFilters(OrgB);

        var labelFiltersA = filtersA["label"];
        var labelFiltersB = filtersB["label"];

        // workspace_list for org A must filter server-side on org A's id (and the plugin
        // label), and must NOT carry org B's id — this is what stops workspace_list from
        // returning another tenant's workspaces.
        Assert.True(labelFiltersA[$"hq.org.id={OrgA}"]);
        Assert.DoesNotContain($"hq.org.id={OrgB}", labelFiltersA.Keys);
        Assert.True(labelFiltersB[$"hq.org.id={OrgB}"]);
        Assert.DoesNotContain($"hq.org.id={OrgA}", labelFiltersB.Keys);

        Assert.True(labelFiltersA["hq.plugin=FileStorage"]);
        Assert.True(labelFiltersB["hq.plugin=FileStorage"]);
    }
}
