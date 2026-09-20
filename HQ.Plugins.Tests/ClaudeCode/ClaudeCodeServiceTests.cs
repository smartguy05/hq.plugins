using System.Diagnostics;
using System.Text.Json;
using HQ.Plugins.ClaudeCode;
using HQ.Plugins.ClaudeCode.Models;

namespace HQ.Plugins.Tests.ClaudeCode;

/// <summary>
/// Covers WP6A-3 / WP6A-4 / WP6A-6 (2026-09 security review, cluster P7):
///   - repoUrl / branch / allowedTools were interpolated into a `/bin/bash -c` string with no
///     validation or escaping (shell injection).
///   - the GitHub PAT was attached to the clone URL for any attacker-chosen host, and could leak
///     via unredacted git stderr.
///   - sessionId had no format validation, enabling cross-session/cross-tenant guessing.
///
/// These target <see cref="GitArgValidation"/> directly (no Docker daemon involved) and the
/// public <see cref="ClaudeCodeService"/> methods that must reject a malformed sessionId before
/// ever touching the container layer.
/// </summary>
public class GitArgValidationTests
{
    [Theory]
    [InlineData("https://github.com/org/repo.git")]
    [InlineData("https://github.example.com/org/repo")]
    [InlineData("https://ghe.mycorp.com:8443/org/repo.git")]
    public void ValidateRepoUrl_AcceptsSafeHttpsUrls(string url)
    {
        var ex = Record.Exception(() => GitArgValidation.ValidateRepoUrl(url));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("x; cat /proc/self/environ; #")]
    [InlineData("https://github.com/org/repo.git; cat /etc/passwd")]
    [InlineData("https://github.com/org/repo.git`whoami`")]
    [InlineData("https://github.com/org/repo.git$(whoami)")]
    [InlineData("http://github.com/org/repo.git")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateRepoUrl_RejectsShellMetacharactersAndNonHttps(string url)
    {
        Assert.Throws<ArgumentException>(() => GitArgValidation.ValidateRepoUrl(url));
    }

    [Theory]
    [InlineData("feature/my-fix_1.0")]
    [InlineData("release-2026.09")]
    public void ValidateRef_AcceptsSafeRefNames(string value)
    {
        var ex = Record.Exception(() => GitArgValidation.ValidateRef(value, "branch"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateRef_AllowsEmptyBecauseRefIsOptional(string value)
    {
        var ex = Record.Exception(() => GitArgValidation.ValidateRef(value, "branch"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("main; id")]
    [InlineData("$(whoami)")]
    [InlineData("`id`")]
    [InlineData("-x")]
    [InlineData("--upload-pack=touch /tmp/pwned")]
    public void ValidateRef_RejectsInjectionAttemptsAndFlagLikeValues(string value)
    {
        Assert.Throws<ArgumentException>(() => GitArgValidation.ValidateRef(value, "branch"));
    }

    [Fact]
    public void IsValidSessionId_AcceptsGeneratedFormat()
    {
        var generated = Guid.NewGuid().ToString("N")[..12];
        Assert.True(GitArgValidation.IsValidSessionId(generated));
    }

    [Theory]
    [InlineData("abc; rm -rf /")]
    [InlineData("ABCDEF012345")]
    [InlineData("short")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("../../etc/passwd")]
    public void IsValidSessionId_RejectsMalformedValues(string sessionId)
    {
        Assert.False(GitArgValidation.IsValidSessionId(sessionId));
    }

    [Fact]
    public void EscapeShellSingleQuoted_ProducesLiteralArgumentUnderRealBash()
    {
        // A payload designed to break out of naive single-quoting and run extra commands.
        const string malicious = "Read' ; id ; '$(whoami)`ls`";
        var escaped = GitArgValidation.EscapeShellSingleQuoted(malicious);

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"printf '%s' {escaped}");

        using var proc = Process.Start(psi);
        Assert.NotNull(proc);
        var output = proc!.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);

        // The whole payload must come back byte-for-byte as DATA, proving nothing inside it
        // was interpreted as a command separator, substitution, or backtick expansion.
        Assert.Equal(malicious, output);
    }

    [Theory]
    [InlineData("github.com", null, true)]
    [InlineData("GitHub.com", null, true)]
    [InlineData("attacker.example", null, false)]
    [InlineData("ghe.mycorp.com", "ghe.mycorp.com", true)]
    [InlineData("attacker.example", "ghe.mycorp.com", false)]
    [InlineData(null, "ghe.mycorp.com", false)]
    public void IsAllowedGitHost_OnlyAllowsGithubOrConfiguredHost(string host, string configuredHost, bool expected)
    {
        Assert.Equal(expected, GitArgValidation.IsAllowedGitHost(host, configuredHost));
    }

    [Fact]
    public void RedactSecrets_RemovesEveryOccurrenceOfEachSecret()
    {
        const string token = "ghp_SuperSecretToken123";
        const string apiKey = "sk-ant-SuperSecretKey456";
        var text = $"remote: fatal auth failed for x-access-token:{token}@attacker.example ({apiKey} also leaked twice: {apiKey})";

        var redacted = GitArgValidation.RedactSecrets(text, token, apiKey);

        Assert.DoesNotContain(token, redacted);
        Assert.DoesNotContain(apiKey, redacted);
        Assert.Contains("***REDACTED***", redacted);
    }

    [Fact]
    public void RedactSecrets_IgnoresNullOrEmptySecretsAndText()
    {
        Assert.Null(GitArgValidation.RedactSecrets(null, "secret"));
        Assert.Equal("", GitArgValidation.RedactSecrets("", "secret"));
        Assert.Equal("hello", GitArgValidation.RedactSecrets("hello", null, ""));
    }

    [Fact]
    public void RedactSecrets_AlsoRedactsUrlEncodedFormOfSecret()
    {
        // WP6A-4 (minor, adversarial re-review): a plain substring match only catches the secret
        // verbatim. A PAT containing characters a URL must percent-encode (e.g. '/') can reach a
        // diagnostic message in its encoded form instead — that must be redacted too.
        const string token = "ghp_super/secret+token";
        var encoded = Uri.EscapeDataString(token);
        var text = $"remote: fatal: could not read Username for 'https://x-access-token:{encoded}@github.com'";

        var redacted = GitArgValidation.RedactSecrets(text, token);

        Assert.DoesNotContain(encoded, redacted);
        Assert.DoesNotContain(token, redacted);
        Assert.Contains("***REDACTED***", redacted);
    }
}

/// <summary>
/// Session-id-consuming tool methods must reject a malformed sessionId before ever calling into
/// <see cref="ContainerManager"/> (which would otherwise try to reach a Docker daemon). Every one
/// of these constructs a real <see cref="ClaudeCodeService"/> with no Docker socket available and
/// asserts it never gets that far for a bad id — a hard proof the guard runs first.
/// </summary>
public class ClaudeCodeServiceSessionValidationTests
{
    private static ClaudeCodeService CreateService() =>
        new(new ServiceConfig { Name = "claude-code" }, (_, _, _) => Task.CompletedTask);

    private static JsonDocument AsJson(object result) =>
        JsonDocument.Parse(JsonSerializer.Serialize(result));

    [Theory]
    [InlineData("not-a-valid-session-id; rm -rf /")]
    [InlineData("")]
    [InlineData("ABCDEF012345")]
    public async Task DestroySession_RejectsMalformedSessionId(string sessionId)
    {
        var service = CreateService();

        var result = await service.DestroySession(
            new ServiceConfig(), new DestroySessionArgs { SessionId = sessionId });

        using var doc = AsJson(result);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
    }

    [Fact]
    public async Task GetDiff_RejectsMalformedSessionId()
    {
        var service = CreateService();

        var result = await service.GetDiff(
            new ServiceConfig(), new GetDiffArgs { SessionId = "; cat /etc/shadow #" });

        using var doc = AsJson(result);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
    }

    [Fact]
    public async Task ContinueSession_RejectsMalformedSessionId()
    {
        var service = CreateService();

        var result = await service.ContinueSession(
            new ServiceConfig(),
            new ContinueArgs { SessionId = "$(id)", Prompt = "do something" });

        using var doc = AsJson(result);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
    }

    [Fact]
    public async Task RunTask_RejectsExplicitMalformedSessionId()
    {
        var service = CreateService();

        var result = await service.RunTask(
            new ServiceConfig(),
            new TaskArgs { Prompt = "hi", SessionId = "`id`" });

        using var doc = AsJson(result);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
    }

    [Fact]
    public async Task RunTask_RejectsInjectionInRepoUrlBeforeTouchingDocker()
    {
        var service = CreateService();

        // A well-formed sessionId is supplied so the only thing under test is repoUrl
        // validation; ArgumentException from validation must surface as a Success=false result
        // via the plugin's own catch, not an unhandled exception.
        var ex = await Record.ExceptionAsync(() => service.RunTask(
            new ServiceConfig(),
            new TaskArgs
            {
                Prompt = "hi",
                SessionId = Guid.NewGuid().ToString("N")[..12],
                RepoUrl = "x; cat /proc/self/environ; #"
            }));

        Assert.IsType<ArgumentException>(ex);
    }
}
