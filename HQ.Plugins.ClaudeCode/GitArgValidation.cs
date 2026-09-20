using System.Text.RegularExpressions;

namespace HQ.Plugins.ClaudeCode;

/// <summary>
/// Pure validation/escaping helpers for the git and Claude Code CLI arguments that
/// <see cref="ClaudeCodeService"/> builds from LLM-supplied tool arguments
/// (repoUrl, branch, baseBranch, allowedTools, sessionId).
///
/// These exist as a separate, dependency-free static class (no Docker) specifically so
/// they can be unit tested without a Docker daemon — the container-exec path they feed
/// (see <see cref="ContainerManager"/>) is inert in this test environment.
///
/// See WP6A-3 / WP6A-4 / WP6A-6 (2026-09 security review, cluster P7):
/// repoUrl/branch/allowedTools were interpolated into a `/bin/bash -c` string without
/// validation or escaping, the GitHub PAT was attached to any attacker-chosen host, and
/// sessionId had no format validation at all.
/// </summary>
internal static class GitArgValidation
{
    // Deliberately conservative: only characters a legitimate https clone URL needs.
    // Rejects shell metacharacters (`;`, `|`, `&`, `$`, backticks, quotes, whitespace, etc.)
    // outright rather than trying to escape them.
    private static readonly Regex SafeRepoUrlPattern =
        new(@"^https://[A-Za-z0-9._\-/:@]+$", RegexOptions.Compiled);

    // Git ref names: letters, digits, dot, underscore, slash, hyphen.
    private static readonly Regex SafeRefPattern =
        new(@"^[A-Za-z0-9._/-]+$", RegexOptions.Compiled);

    // Matches exactly the format RunTask generates: Guid.NewGuid().ToString("N")[..12].
    private static readonly Regex SessionIdPattern =
        new(@"^[a-f0-9]{12}$", RegexOptions.Compiled);

    /// <summary>Throws <see cref="ArgumentException"/> unless <paramref name="repoUrl"/> is a
    /// well-formed, absolute https:// URL built only from characters that are safe to place
    /// inside a git argv element (no shell metacharacters).</summary>
    public static void ValidateRepoUrl(string repoUrl)
    {
        if (string.IsNullOrWhiteSpace(repoUrl))
            throw new ArgumentException("repoUrl is required.");

        if (!SafeRepoUrlPattern.IsMatch(repoUrl))
            throw new ArgumentException(
                "repoUrl must be an https:// URL containing only letters, digits, '.', '_', '-', '/', ':' and '@'.");

        if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("repoUrl must be a valid absolute https:// URL.");
    }

    /// <summary>Throws <see cref="ArgumentException"/> unless <paramref name="value"/> is empty
    /// (refs are optional — callers decide whether that's acceptable) or a safe git ref name.
    /// Rejects values starting with '-' so they can never be mistaken for a git CLI flag when
    /// passed positionally.</summary>
    public static void ValidateRef(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (value.StartsWith('-'))
            throw new ArgumentException($"{paramName} must not start with '-'.");

        if (!SafeRefPattern.IsMatch(value))
            throw new ArgumentException($"{paramName} must contain only letters, digits, '.', '_', '/', '-'.");
    }

    /// <summary>True only for the exact 12-lowercase-hex-character format RunTask generates.</summary>
    public static bool IsValidSessionId(string sessionId)
        => !string.IsNullOrWhiteSpace(sessionId) && SessionIdPattern.IsMatch(sessionId);

    /// <summary>Wraps <paramref name="value"/> in single quotes for embedding in a
    /// `/bin/bash -c` string, escaping any embedded single quote so it cannot break out of the
    /// quoting (the classic `'\''` idiom). Mirrors the escaping RunClaudeCode already applied to
    /// the prompt/system-prompt arguments, extended to allowedTools.</summary>
    public static string EscapeShellSingleQuoted(string value)
        => "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";

    /// <summary>True when <paramref name="host"/> is github.com or matches the tenant-configured
    /// GitHub Enterprise host. Used to decide whether the GitHub PAT may be attached to a clone
    /// URL — never for an arbitrary attacker-chosen host.</summary>
    public static bool IsAllowedGitHost(string host, string configuredHost)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        if (string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(configuredHost) &&
               string.Equals(host, configuredHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Replaces every occurrence of each non-empty secret in <paramref name="text"/> with
    /// a fixed redaction marker. Used to keep the GitHub PAT / Anthropic API key out of exception
    /// messages and tool results built from container stdout/stderr.
    ///
    /// WP6A-4 (minor, adversarial re-review, 2026-09): a plain substring match only catches the
    /// secret verbatim. git/credential-helper diagnostics can echo it URL-encoded instead — e.g.
    /// a PAT containing '/' or '+' embedded in a Basic-auth URL — so also redact each secret's
    /// <see cref="Uri.EscapeDataString(string)"/> form. This is defense-in-depth, not a complete
    /// fix: a partially truncated or otherwise transformed secret can still slip through, since
    /// there is no general way to recognize an arbitrary derived encoding of an opaque token.
    /// </summary>
    public static string RedactSecrets(string text, params string[] secrets)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        foreach (var secret in secrets)
        {
            if (string.IsNullOrEmpty(secret))
                continue;

            text = text.Replace(secret, "***REDACTED***");

            var encoded = Uri.EscapeDataString(secret);
            if (!string.Equals(encoded, secret, StringComparison.Ordinal))
                text = text.Replace(encoded, "***REDACTED***");
        }

        return text;
    }
}
