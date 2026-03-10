using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.ClaudeCode.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Anthropic API key passed as ANTHROPIC_API_KEY env var to the container.")]
    public string AnthropicApiKey { get; set; }

    [Tooltip("Model for Claude Code to use (default: claude-sonnet-4-6).")]
    public string Model { get; set; } = "claude-sonnet-4-6";

    [Tooltip("Docker image with Claude Code pre-installed (default: hq-claude-code:latest).")]
    public string DockerImage { get; set; } = "hq-claude-code:latest";

    [Tooltip("Docker socket URI. Leave empty to auto-detect.")]
    public string DockerHost { get; set; }

    [Tooltip("Default max agentic iterations per task (default: 25).")]
    public int MaxTurns { get; set; } = 25;

    [Tooltip("Max execution time per task in seconds (default: 600).")]
    public int TimeoutSeconds { get; set; } = 600;

    [Tooltip("Base directory inside container for repos (default: /workspace).")]
    public string CloneBaseDir { get; set; } = "/workspace";

    [Tooltip("Default tool allowlist for Claude Code.")]
    public string AllowedTools { get; set; } = "Bash,Read,Edit,Write,Glob,Grep";

    [Tooltip("GitHub PAT for clone/push/PR operations.")]
    public string GitHubToken { get; set; }

    [Tooltip("Optional comma-separated hostnames to allow. Empty = all traffic allowed.")]
    public string NetworkWhitelist { get; set; }

    [Tooltip("Optional comma-separated hostnames to block.")]
    public string NetworkBlacklist { get; set; }

    [Tooltip("Container memory limit in MB (default: 2048).")]
    public long MemoryLimitMb { get; set; } = 2048;

    [Tooltip("Container PID limit (default: 256).")]
    public long PidsLimit { get; set; } = 256;

    [Tooltip("Container CPU shares (default: 512).")]
    public long CpuShares { get; set; } = 512;
}
