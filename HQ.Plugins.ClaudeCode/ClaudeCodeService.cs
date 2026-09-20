using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.ClaudeCode.Models;

namespace HQ.Plugins.ClaudeCode;

public class ClaudeCodeService
{
    private readonly ServiceConfig _config;
    private readonly LogDelegate _logger;
    private readonly ContainerManager _containers;
    private INotificationService _notificationService;

    public ClaudeCodeService(ServiceConfig config, LogDelegate logger)
    {
        _config = config;
        _logger = logger;
        _containers = new ContainerManager(config);
    }

    /// <summary>Test-only entry point: lets HQ.Plugins.Tests supply a <see cref="ContainerManager"/>
    /// built on a mocked Docker client (see <c>ContainerManagerOwnershipTests</c>) so the WP6A-6
    /// ownership gate below can be exercised end-to-end without a live daemon.</summary>
    internal ClaudeCodeService(ServiceConfig config, LogDelegate logger, ContainerManager containers)
    {
        _config = config;
        _logger = logger;
        _containers = containers;
    }

    public Task<object> ProcessRequest(object rawServiceRequest, ServiceConfig config, INotificationService notificationService)
    {
        _notificationService = notificationService;
        return this.ProcessRequest<ClaudeCodeService>(rawServiceRequest, config, notificationService);
    }

    // ───────────────────────────── Tools ─────────────────────────────

    [Display(Name = ClaudeCodeMethods.Task)]
    [Description("Run a coding task with Claude Code. Clones a repo (if needed), prompts Claude Code to do the work, and returns structured JSON results. Use for bug fixes, new features, refactoring, tests, etc.")]
    [Parameters(typeof(TaskArgs))]
    public async Task<object> RunTask(ServiceConfig config, TaskArgs request)
    {
        // WP6A-6: format-only check here, deliberately NOT the full ValidateSessionAsync
        // ownership gate used below for Continue/GetDiff/etc. — that gate calls into
        // ContainerManager (a Docker round trip) and must not run before the repoUrl/branch
        // validation immediately below (see RunTask_RejectsInjectionInRepoUrlBeforeTouchingDocker,
        // which asserts a bad repoUrl is rejected before anything touches Docker at all). The
        // ownership check for a supplied/resumed sessionId still happens — just one line later,
        // inside ContainerManager.EnsureContainerAsync, which throws rather than silently
        // starting/reusing another config's container.
        if (!string.IsNullOrWhiteSpace(request.SessionId) && !GitArgValidation.IsValidSessionId(request.SessionId))
            return new { Success = false, Message = "sessionId is invalid" };

        // WP6A-3: validate repoUrl/branch/baseBranch BEFORE ever touching the container/Docker
        // layer — they used to be interpolated straight into a `/bin/bash -c` string.
        if (!string.IsNullOrWhiteSpace(request.RepoUrl))
        {
            GitArgValidation.ValidateRepoUrl(request.RepoUrl);
            GitArgValidation.ValidateRef(request.Branch, "branch");
            GitArgValidation.ValidateRef(request.BaseBranch, "baseBranch");
        }

        var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N")[..12];
        await _containers.EnsureContainerAsync(sessionId);

        // Clone repo if provided
        if (!string.IsNullOrWhiteSpace(request.RepoUrl))
        {
            await CloneRepo(sessionId, request.RepoUrl, request.Branch, request.BaseBranch);
        }

        // Run Claude Code
        var result = await RunClaudeCode(sessionId, request.Prompt, request.MaxTurns, request.AllowedTools, request.SystemPrompt);
        result.SessionId = sessionId;
        return result;
    }

    [Display(Name = ClaudeCodeMethods.Continue)]
    [Description("Continue a previous Claude Code session with a follow-up prompt. Uses --resume to maintain context. For multi-step workflows like 'now write tests for what you just built.'")]
    [Parameters(typeof(ContinueArgs))]
    public async Task<object> ContinueSession(ServiceConfig config, ContinueArgs request)
    {
        var sessionError = await ValidateSessionAsync(request.SessionId);
        if (sessionError != null)
            return sessionError;

        var result = await RunClaudeCode(request.SessionId, request.Prompt, request.MaxTurns, request.AllowedTools, null, resume: true);
        result.SessionId = request.SessionId;
        return result;
    }

    [Display(Name = ClaudeCodeMethods.Review)]
    [Description("Ask Claude Code to review its own changes against criteria (security, style, correctness). Returns structured pass/fail assessment.")]
    [Parameters(typeof(ReviewArgs))]
    public async Task<object> ReviewChanges(ServiceConfig config, ReviewArgs request)
    {
        var sessionError = await ValidateSessionAsync(request.SessionId);
        if (sessionError != null)
            return sessionError;

        var reviewPrompt = string.IsNullOrWhiteSpace(request.Prompt)
            ? "Review the current git diff. Check for: 1) Security vulnerabilities 2) Correctness issues 3) Edge cases not handled 4) Code style problems. Return a JSON object with fields: passed (bool), issues (array of {severity, description, file, line}), summary (string)."
            : request.Prompt;

        var result = await RunClaudeCode(request.SessionId, reviewPrompt, null, null, null);
        result.SessionId = request.SessionId;
        return result;
    }

    [Display(Name = ClaudeCodeMethods.Status)]
    [Description("Check if a Claude Code session container is running and get its status.")]
    [Parameters(typeof(StatusArgs))]
    public async Task<object> GetStatus(ServiceConfig config, StatusArgs request)
    {
        var sessionError = await ValidateSessionAsync(request.SessionId);
        if (sessionError != null)
            return sessionError;

        var status = await _containers.GetContainerStatusAsync(request.SessionId);
        return new { Success = true, SessionId = request.SessionId, ContainerStatus = status };
    }

    [Display(Name = ClaudeCodeMethods.GetDiff)]
    [Description("Get the current git diff from the container without prompting Claude Code. Useful for inspecting changes before approving a PR.")]
    [Parameters(typeof(GetDiffArgs))]
    public async Task<object> GetDiff(ServiceConfig config, GetDiffArgs request)
    {
        var sessionError = await ValidateSessionAsync(request.SessionId);
        if (sessionError != null)
            return sessionError;

        var repoDir = $"{_config.CloneBaseDir}/repo";
        var (stdout, stderr, exitCode) = await _containers.ExecAsync(
            request.SessionId, "git diff HEAD", repoDir, 30);

        if (exitCode != 0)
        {
            // Maybe no git repo at /workspace/repo, try /workspace
            (stdout, stderr, exitCode) = await _containers.ExecAsync(
                request.SessionId, "git diff HEAD", _config.CloneBaseDir, 30);
        }

        return new TaskResult
        {
            Success = exitCode == 0,
            SessionId = request.SessionId,
            Diff = stdout,
            Error = exitCode != 0 ? Redact(stderr) : null,
            ExitCode = exitCode
        };
    }

    [Display(Name = ClaudeCodeMethods.CreatePr)]
    [Description("Prompt Claude Code to commit, push, and create a PR. Uses confirmation flow since this is externally visible. The HQ agent should review the diff first via claude_code_get_diff.")]
    [Parameters(typeof(CreatePrArgs))]
    public async Task<object> CreatePr(ServiceConfig config, CreatePrArgs request)
    {
        var notificationService = _notificationService;
        var sessionError = await ValidateSessionAsync(request.SessionId);
        if (sessionError != null)
            return sessionError;

        // Confirmation flow — first call triggers confirmation, second executes
        if (string.IsNullOrWhiteSpace(request.ConfirmationId))
        {
            // Get diff for confirmation preview
            var repoDir = $"{_config.CloneBaseDir}/repo";
            var (diffOutput, _, _) = await _containers.ExecAsync(
                request.SessionId, "git diff HEAD", repoDir, 30);

            var confirmation = new Confirmation
            {
                ConfirmationMessage = "Review the diff and approve PR creation:",
                Content = TruncateForConfirmation(diffOutput),
                Options = new Dictionary<string, bool>
                {
                    { "Approve PR", true },
                    { "Reject", false }
                },
                Id = Guid.NewGuid()
            };
            request.ConfirmationId = confirmation.Id.ToString();
            var confirmResult = await notificationService.RequestConfirmation("Claude Code", confirmation, request);
            var isSuccessful = (bool?)confirmResult.GetType().GetProperty("Success")?.GetValue(confirmResult) ?? false;

            if (isSuccessful)
                return new { Success = true, ConfirmationId = confirmation.Id.ToString(), SessionId = request.SessionId };

            return new { Success = false, Message = "Failed to send confirmation request" };
        }

        if (!notificationService.DoesConfirmationExist(Guid.Parse(request.ConfirmationId), out _))
            return new { Success = false, Error = "PR creation requires valid confirmation" };

        // Execute PR creation via Claude Code
        var prPrompt = string.IsNullOrWhiteSpace(request.Prompt)
            ? "Commit all changes with a descriptive message, push the branch, and create a pull request."
            : request.Prompt;

        var result = await RunClaudeCode(request.SessionId, prPrompt, null, null, null);
        result.SessionId = request.SessionId;
        return result;
    }

    [Display(Name = ClaudeCodeMethods.DestroySession)]
    [Description("Stop and remove the container and volume for a Claude Code session. Call when the workflow is complete or abandoned.")]
    [Parameters(typeof(DestroySessionArgs))]
    public async Task<object> DestroySession(ServiceConfig config, DestroySessionArgs request)
    {
        var sessionError = await ValidateSessionAsync(request.SessionId);
        if (sessionError != null)
            return sessionError;

        await _containers.DestroyContainerAsync(request.SessionId);
        return new { Success = true, SessionId = request.SessionId, Message = "Session destroyed" };
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    /// <summary>
    /// Gate run by every tool method that operates on an *existing* session (Continue, Review,
    /// GetStatus, GetDiff, CreatePr, DestroySession). Returns a non-null error result — never
    /// null — unless the caller may proceed.
    ///
    /// WP6A-6 (adversarial re-review, 2026-09): a format check alone (GitArgValidation.
    /// IsValidSessionId) is NOT ownership — every legitimately generated sessionId matches
    /// ^[a-f0-9]{12}$ for every tenant, so a leaked, well-formed sessionId from another org used
    /// to sail straight through it and reach the container layer (the actual repro: obtain
    /// another org's leaked sessionId and call claude_code_get_diff / claude_code_continue /
    /// claude_code_destroy_session). ContainerManager.IsSessionAccessibleAsync additionally
    /// checks the container's owner label — a fingerprint of the ServiceConfig that created the
    /// session — against this caller's own config, so a session created under a different
    /// config/tenant is rejected here too, with the SAME generic "sessionId is invalid" message
    /// used for a malformed id, so the response never confirms whether the session exists.
    ///
    /// This is a config-identity heuristic, not a cryptographic OrgId guarantee — see the
    /// remarks on ContainerManager's LabelOwner for exactly what it does and doesn't close, and
    /// why hq.plugins can't do better than this without the same core plugin-invocation-contract
    /// change WP6A-5 was deferred behind.
    /// </summary>
    private async Task<object> ValidateSessionAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return new { Success = false, Message = "sessionId is required" };

        if (!GitArgValidation.IsValidSessionId(sessionId))
            return new { Success = false, Message = "sessionId is invalid" };

        if (!await _containers.IsSessionAccessibleAsync(sessionId))
            return new { Success = false, Message = "sessionId is invalid" };

        return null;
    }

    /// <summary>WP6A-4: redacts the configured secrets out of container stdout/stderr before it
    /// can reach an exception message or tool result the model/user sees.</summary>
    private string Redact(string text) =>
        GitArgValidation.RedactSecrets(text, _config.GitHubToken, _config.AnthropicApiKey);

    private async Task CloneRepo(string sessionId, string repoUrl, string branch, string baseBranch)
    {
        // Callers must have already run GitArgValidation.ValidateRepoUrl/ValidateRef — this is
        // the last line of defense, not the primary check.
        GitArgValidation.ValidateRepoUrl(repoUrl);
        GitArgValidation.ValidateRef(branch, "branch");
        GitArgValidation.ValidateRef(baseBranch, "baseBranch");

        var cloneDir = $"{_config.CloneBaseDir}/repo";

        // Check if already cloned. cloneDir is built entirely from server-side config, never
        // from caller input, so a plain bash -c test is safe here.
        var (_, _, checkExit) = await _containers.ExecAsync(sessionId, $"test -d {cloneDir}/.git", "/", 10);
        if (checkExit == 0)
        {
            // Already cloned, just fetch
            await _containers.ExecArgvAsync(sessionId, ["git", "fetch", "--all"], cloneDir, 60);
        }
        else
        {
            // WP6A-3: git operations now run as argv (no shell), so repoUrl/branch cannot be
            // reinterpreted as shell syntax regardless of their content.
            var uri = new Uri(repoUrl);
            var cloneUrl = repoUrl;

            // WP6A-4: only attach the PAT when the host is github.com or the tenant-configured
            // GHE host — never to an arbitrary attacker-chosen host from repoUrl.
            if (!string.IsNullOrWhiteSpace(_config.GitHubToken) &&
                GitArgValidation.IsAllowedGitHost(uri.Host, _config.GitHubHost))
            {
                cloneUrl = $"https://x-access-token:{_config.GitHubToken}@{uri.Host}{uri.PathAndQuery}";
            }

            var (_, stderr, exitCode) = await _containers.ExecArgvAsync(
                sessionId, ["git", "clone", cloneUrl, cloneDir], _config.CloneBaseDir, 120);
            if (exitCode != 0)
                // WP6A-4: git stderr can echo the remote URL (which may embed the PAT) back —
                // redact known secrets before this reaches the model/user.
                throw new InvalidOperationException($"git clone failed: {Redact(stderr)}");
        }

        // Checkout branch if specified
        if (!string.IsNullOrWhiteSpace(branch))
        {
            var baseRef = string.IsNullOrWhiteSpace(baseBranch) ? "origin/main" : $"origin/{baseBranch}";

            // Try to checkout existing branch first, then create new from baseRef, then from HEAD.
            var (_, _, branchExit) = await _containers.ExecArgvAsync(
                sessionId, ["git", "checkout", branch], cloneDir, 30);
            if (branchExit != 0)
            {
                var (_, _, createExit) = await _containers.ExecArgvAsync(
                    sessionId, ["git", "checkout", "-b", branch, baseRef], cloneDir, 30);
                if (createExit != 0)
                {
                    // Fall back to creating from HEAD
                    await _containers.ExecArgvAsync(sessionId, ["git", "checkout", "-b", branch], cloneDir, 30);
                }
            }
        }

        // Configure git identity
        await _containers.ExecArgvAsync(sessionId,
            ["git", "config", "user.email", "hq-agent@automated.dev"], cloneDir, 10);
        await _containers.ExecArgvAsync(sessionId,
            ["git", "config", "user.name", "HQ Agent"], cloneDir, 10);
    }

    private async Task<TaskResult> RunClaudeCode(string sessionId, string prompt, int? maxTurnsOverride, string allowedToolsOverride, string systemPrompt, bool resume = false)
    {
        var maxTurns = maxTurnsOverride ?? _config.MaxTurns;
        var allowedTools = allowedToolsOverride ?? _config.AllowedTools;
        var outputFormat = "json";
        var timeout = _config.TimeoutSeconds;

        // Build claude command
        var cmd = new StringBuilder();
        cmd.Append("claude -p");

        // Escape prompt for shell
        var escapedPrompt = prompt.Replace("'", "'\\''");
        cmd.Append($" '{escapedPrompt}'");

        cmd.Append($" --output-format {outputFormat}");
        cmd.Append($" --max-turns {maxTurns}");
        cmd.Append($" --model {_config.Model}");

        if (!string.IsNullOrWhiteSpace(allowedTools))
        {
            var tools = allowedTools.Split(',', StringSplitOptions.TrimEntries);
            foreach (var tool in tools)
                // WP6A-3: unlike the prompt/system-prompt below, this was single-quoted without
                // escaping embedded single quotes, so e.g. "Read' ; id ; '" broke out of the
                // quoting and ran arbitrary shell commands (and widened Claude Code's own
                // --allowedTools flag with whatever followed).
                cmd.Append($" --allowedTools {GitArgValidation.EscapeShellSingleQuoted(tool)}");
        }

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            var escapedSystem = systemPrompt.Replace("'", "'\\''");
            cmd.Append($" --append-system-prompt '{escapedSystem}'");
        }

        if (resume)
            cmd.Append(" --resume");

        // Determine working directory — use repo dir if it exists
        var workingDir = $"{_config.CloneBaseDir}/repo";
        var (_, _, dirCheck) = await _containers.ExecAsync(sessionId, $"test -d {workingDir}", "/", 5);
        if (dirCheck != 0)
            workingDir = _config.CloneBaseDir;

        var (stdout, stderr, exitCode) = await _containers.ExecAsync(sessionId, cmd.ToString(), workingDir, timeout);

        return new TaskResult
        {
            Success = exitCode == 0,
            Output = stdout,
            Error = exitCode != 0 ? Redact(stderr) : null,
            ExitCode = exitCode
        };
    }

    private static string TruncateForConfirmation(string text, int maxLength = 2000)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(no changes)";
        return text.Length <= maxLength ? text : text[..maxLength] + "\n... (truncated)";
    }
}
