using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.ClaudeCode.Models;

namespace HQ.Plugins.ClaudeCode;

public class ClaudeCodeService
{
    private readonly ServiceConfig _config;
    private readonly LogDelegate _logger;
    private readonly ContainerManager _containers;

    public ClaudeCodeService(ServiceConfig config, LogDelegate logger)
    {
        _config = config;
        _logger = logger;
        _containers = new ContainerManager(config);
    }

    public async Task<object> ProcessRequest(ServiceRequest request, ServiceConfig config, INotificationService notificationService)
    {
        return request.Method switch
        {
            ClaudeCodeMethods.Task => await RunTask(request),
            ClaudeCodeMethods.Continue => await ContinueSession(request),
            ClaudeCodeMethods.Review => await ReviewChanges(request),
            ClaudeCodeMethods.Status => await GetStatus(request),
            ClaudeCodeMethods.GetDiff => await GetDiff(request),
            ClaudeCodeMethods.CreatePr => await CreatePr(request, notificationService),
            ClaudeCodeMethods.DestroySession => await DestroySession(request),
            _ => new { Success = false, Message = $"Unknown method: {request.Method}" }
        };
    }

    // ───────────────────────────── Tools ─────────────────────────────

    [Display(Name = ClaudeCodeMethods.Task)]
    [Description("Run a coding task with Claude Code. Clones a repo (if needed), prompts Claude Code to do the work, and returns structured JSON results. Use for bug fixes, new features, refactoring, tests, etc.")]
    [Parameters("""{"type":"object","properties":{"prompt":{"type":"string","description":"The task/instruction for Claude Code (e.g. 'Fix the auth bug in login.ts and add tests')"},"repoUrl":{"type":"string","description":"GitHub repo URL to clone (e.g. 'https://github.com/org/repo'). Optional if repo already cloned in session."},"branch":{"type":"string","description":"Branch to checkout or create for the work"},"baseBranch":{"type":"string","description":"Base branch for new branch creation (default: main)"},"sessionId":{"type":"string","description":"Session ID to reuse an existing container. If omitted, a new session is created."},"maxTurns":{"type":"integer","description":"Override max agentic iterations (default from config)"},"allowedTools":{"type":"string","description":"Override tool allowlist (comma-separated, e.g. 'Bash,Read,Edit,Write')"},"systemPrompt":{"type":"string","description":"Additional system prompt to append (e.g. coding standards, constraints)"}},"required":["prompt"]}""")]
    public async Task<object> RunTask(ServiceRequest request)
    {
        var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N")[..12];
        await _containers.EnsureContainerAsync(sessionId);

        // Clone repo if provided
        if (!string.IsNullOrWhiteSpace(request.RepoUrl))
        {
            await CloneRepo(sessionId, request.RepoUrl, request.Branch, request.BaseBranch);
        }

        // Run Claude Code
        var result = await RunClaudeCode(sessionId, request.Prompt, request);
        result.SessionId = sessionId;
        return result;
    }

    [Display(Name = ClaudeCodeMethods.Continue)]
    [Description("Continue a previous Claude Code session with a follow-up prompt. Uses --resume to maintain context. For multi-step workflows like 'now write tests for what you just built.'")]
    [Parameters("""{"type":"object","properties":{"sessionId":{"type":"string","description":"Session ID from a previous claude_code_task call"},"prompt":{"type":"string","description":"Follow-up instruction (e.g. 'Now write tests for the changes you just made')"},"maxTurns":{"type":"integer","description":"Override max agentic iterations"},"allowedTools":{"type":"string","description":"Override tool allowlist"}},"required":["sessionId","prompt"]}""")]
    public async Task<object> ContinueSession(ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            return new { Success = false, Message = "sessionId is required" };

        var result = await RunClaudeCode(request.SessionId, request.Prompt, request, resume: true);
        result.SessionId = request.SessionId;
        return result;
    }

    [Display(Name = ClaudeCodeMethods.Review)]
    [Description("Ask Claude Code to review its own changes against criteria (security, style, correctness). Returns structured pass/fail assessment.")]
    [Parameters("""{"type":"object","properties":{"sessionId":{"type":"string","description":"Session ID of the container with changes to review"},"prompt":{"type":"string","description":"Review criteria (e.g. 'Check for security vulnerabilities, edge cases, and code style issues'). If omitted, uses a default review prompt."}},"required":["sessionId"]}""")]
    public async Task<object> ReviewChanges(ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            return new { Success = false, Message = "sessionId is required" };

        var reviewPrompt = string.IsNullOrWhiteSpace(request.Prompt)
            ? "Review the current git diff. Check for: 1) Security vulnerabilities 2) Correctness issues 3) Edge cases not handled 4) Code style problems. Return a JSON object with fields: passed (bool), issues (array of {severity, description, file, line}), summary (string)."
            : request.Prompt;

        var result = await RunClaudeCode(request.SessionId, reviewPrompt, request);
        result.SessionId = request.SessionId;
        return result;
    }

    [Display(Name = ClaudeCodeMethods.Status)]
    [Description("Check if a Claude Code session container is running and get its status.")]
    [Parameters("""{"type":"object","properties":{"sessionId":{"type":"string","description":"Session ID to check"}},"required":["sessionId"]}""")]
    public async Task<object> GetStatus(ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            return new { Success = false, Message = "sessionId is required" };

        var status = await _containers.GetContainerStatusAsync(request.SessionId);
        return new { Success = true, SessionId = request.SessionId, ContainerStatus = status };
    }

    [Display(Name = ClaudeCodeMethods.GetDiff)]
    [Description("Get the current git diff from the container without prompting Claude Code. Useful for inspecting changes before approving a PR.")]
    [Parameters("""{"type":"object","properties":{"sessionId":{"type":"string","description":"Session ID of the container with changes"}},"required":["sessionId"]}""")]
    public async Task<object> GetDiff(ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            return new { Success = false, Message = "sessionId is required" };

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
            Error = exitCode != 0 ? stderr : null,
            ExitCode = exitCode
        };
    }

    [Display(Name = ClaudeCodeMethods.CreatePr)]
    [Description("Prompt Claude Code to commit, push, and create a PR. Uses confirmation flow since this is externally visible. The HQ agent should review the diff first via claude_code_get_diff.")]
    [Parameters("""{"type":"object","properties":{"sessionId":{"type":"string","description":"Session ID of the container with changes to commit and push"},"prompt":{"type":"string","description":"PR creation instructions (e.g. 'Commit all changes and create a PR titled Fix #42: auth bug')"}},"required":["sessionId","prompt"]}""")]
    public async Task<object> CreatePr(ServiceRequest request, INotificationService notificationService)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            return new { Success = false, Message = "sessionId is required" };

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

        var result = await RunClaudeCode(request.SessionId, prPrompt, request);
        result.SessionId = request.SessionId;
        return result;
    }

    [Display(Name = ClaudeCodeMethods.DestroySession)]
    [Description("Stop and remove the container and volume for a Claude Code session. Call when the workflow is complete or abandoned.")]
    [Parameters("""{"type":"object","properties":{"sessionId":{"type":"string","description":"Session ID to destroy"}},"required":["sessionId"]}""")]
    public async Task<object> DestroySession(ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            return new { Success = false, Message = "sessionId is required" };

        await _containers.DestroyContainerAsync(request.SessionId);
        return new { Success = true, SessionId = request.SessionId, Message = "Session destroyed" };
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private async Task CloneRepo(string sessionId, string repoUrl, string branch, string baseBranch)
    {
        var cloneDir = $"{_config.CloneBaseDir}/repo";

        // Check if already cloned
        var (_, _, checkExit) = await _containers.ExecAsync(sessionId, $"test -d {cloneDir}/.git", "/", 10);
        if (checkExit == 0)
        {
            // Already cloned, just fetch and checkout
            await _containers.ExecAsync(sessionId, "git fetch --all", cloneDir, 60);
        }
        else
        {
            // Build clone URL with token auth if available
            var cloneUrl = repoUrl;
            if (!string.IsNullOrWhiteSpace(_config.GitHubToken) && cloneUrl.StartsWith("https://"))
            {
                var uri = new Uri(cloneUrl);
                cloneUrl = $"https://x-access-token:{_config.GitHubToken}@{uri.Host}{uri.PathAndQuery}";
            }

            var (_, stderr, exitCode) = await _containers.ExecAsync(
                sessionId, $"git clone {cloneUrl} {cloneDir}", _config.CloneBaseDir, 120);
            if (exitCode != 0)
                throw new InvalidOperationException($"git clone failed: {stderr}");
        }

        // Checkout branch if specified
        if (!string.IsNullOrWhiteSpace(branch))
        {
            var baseRef = string.IsNullOrWhiteSpace(baseBranch) ? "origin/main" : $"origin/{baseBranch}";

            // Try to checkout existing branch first, then create new
            var (_, _, branchExit) = await _containers.ExecAsync(
                sessionId, $"git checkout {branch} 2>/dev/null || git checkout -b {branch} {baseRef}", cloneDir, 30);
            if (branchExit != 0)
            {
                // Fall back to creating from HEAD
                await _containers.ExecAsync(sessionId, $"git checkout -b {branch}", cloneDir, 30);
            }
        }

        // Configure git identity
        await _containers.ExecAsync(sessionId,
            "git config user.email 'hq-agent@automated.dev' && git config user.name 'HQ Agent'", cloneDir, 10);
    }

    private async Task<TaskResult> RunClaudeCode(string sessionId, string prompt, ServiceRequest request, bool resume = false)
    {
        var maxTurns = request.MaxTurns ?? _config.MaxTurns;
        var allowedTools = request.AllowedTools ?? _config.AllowedTools;
        var outputFormat = request.OutputFormat ?? "json";
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
                cmd.Append($" --allowedTools '{tool}'");
        }

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            var escapedSystem = request.SystemPrompt.Replace("'", "'\\''");
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
            Error = exitCode != 0 ? stderr : null,
            ExitCode = exitCode
        };
    }

    private static string TruncateForConfirmation(string text, int maxLength = 2000)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(no changes)";
        return text.Length <= maxLength ? text : text[..maxLength] + "\n... (truncated)";
    }
}
