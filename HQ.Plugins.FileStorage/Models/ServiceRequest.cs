using HQ.Models.Interfaces;

namespace HQ.Plugins.FileStorage.Models;

public record ServiceRequest : IPluginServiceRequest
{
    // IPluginServiceRequest
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Workspace
    public string WorkspaceId { get; set; }
    public string TeamId { get; set; }

    // File operations
    public string FilePath { get; set; }
    public string FileContent { get; set; }
    public bool? IsBase64 { get; set; }
    public bool? Recursive { get; set; }

    // Exec
    public string Command { get; set; }
    public string WorkingDirectory { get; set; }
    public string ScriptContent { get; set; }
    public string ScriptType { get; set; }
    public int? TimeoutSeconds { get; set; }

    // Cross-workspace copy
    public string SourceWorkspaceId { get; set; }
    public string DestWorkspaceId { get; set; }
    public string SourcePath { get; set; }
    public string DestPath { get; set; }
}
