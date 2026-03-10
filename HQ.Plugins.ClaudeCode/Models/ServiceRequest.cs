using HQ.Models.Interfaces;

namespace HQ.Plugins.ClaudeCode.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Task prompt
    public string Prompt { get; set; }

    // Repository
    public string RepoUrl { get; set; }
    public string Branch { get; set; }
    public string BaseBranch { get; set; }

    // Session management
    public string SessionId { get; set; }

    // Overrides
    public int? MaxTurns { get; set; }
    public string AllowedTools { get; set; }
    public string OutputFormat { get; set; } = "json";
    public string SystemPrompt { get; set; }
}
