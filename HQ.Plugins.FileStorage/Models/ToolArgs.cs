using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;

namespace HQ.Plugins.FileStorage.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable).
///
/// WP6A-5 (2026-09 security review): every tool that touches a workspace/team volume now carries
/// <c>OrganizationId</c>, following the same convention as HQ.Plugins.Tasks — <c>[Required]
/// Guid? OrganizationId</c> (NOT <c>[Injected]</c>, so it stays visible in the schema like Tasks
/// does). The host (HQ.Services.Plugin.PluginService.InjectOrganizationId) force-overwrites this
/// property's value server-side with the caller's authoritative agent.OrganizationId before the
/// plugin ever sees the call, so any value the model supplies here is discarded. This is what
/// gives FileStorageService a trustworthy tenant identity for workspace/team volume scoping.
/// </summary>

/// <summary>Args for tools that take no LLM parameters other than the injected org id.</summary>
public class ListWorkspacesArgs
{
    [Required, Description("Organisation GUID")]
    public Guid? OrganizationId { get; set; }
}

public class CreateWorkspaceArgs
{
    [Required, Description("Organisation GUID")]
    public Guid? OrganizationId { get; set; }

    [Required, Description("Unique workspace identifier (alphanumeric and hyphens only)")]
    public string WorkspaceId { get; set; }

    [Description("Optional team ID. Workspaces with the same teamId share a read-write volume mounted at /shared.")]
    public string TeamId { get; set; }
}

public class DestroyWorkspaceArgs
{
    [Required, Description("Organisation GUID")]
    public Guid? OrganizationId { get; set; }

    [Required, Description("The workspace ID to destroy")]
    public string WorkspaceId { get; set; }
}

public class WorkspaceStatusArgs
{
    [Required, Description("Organisation GUID")]
    public Guid? OrganizationId { get; set; }

    [Required, Description("The workspace ID to inspect")]
    public string WorkspaceId { get; set; }
}

public class WriteFileArgs
{
    [Required, Description("Organisation GUID")]
    public Guid? OrganizationId { get; set; }

    [Required, Description("The workspace ID")]
    public string WorkspaceId { get; set; }

    [Required, Description("Absolute path inside the container (e.g. /workspace/script.py)")]
    public string FilePath { get; set; }

    [Required, Description("The file content (text or base64-encoded)")]
    public string FileContent { get; set; }

    [Description("Set to true if fileContent is base64-encoded binary data. Defaults to false.")]
    public bool? IsBase64 { get; set; }
}

public class ReadFileArgs
{
    [Required, Description("Organisation GUID")]
    public Guid? OrganizationId { get; set; }

    [Required, Description("The workspace ID")]
    public string WorkspaceId { get; set; }

    [Required, Description("Absolute path inside the container (e.g. /workspace/output.txt)")]
    public string FilePath { get; set; }
}

public class ListFilesArgs
{
    [Required, Description("Organisation GUID")]
    public Guid? OrganizationId { get; set; }

    [Required, Description("The workspace ID")]
    public string WorkspaceId { get; set; }

    [Description("Path to list (defaults to /workspace)")]
    public string FilePath { get; set; }
}

public class DeleteFileArgs
{
    [Required, Description("Organisation GUID")]
    public Guid? OrganizationId { get; set; }

    [Required, Description("The workspace ID")]
    public string WorkspaceId { get; set; }

    [Required, Description("Path to delete")]
    public string FilePath { get; set; }

    [Description("Set to true to recursively delete a directory. Defaults to false.")]
    public bool? Recursive { get; set; }
}

public class ExecCommandArgs
{
    [Required, Description("Organisation GUID")]
    public Guid? OrganizationId { get; set; }

    [Required, Description("The workspace ID")]
    public string WorkspaceId { get; set; }

    [Required, Description("The shell command to execute")]
    public string Command { get; set; }

    [Description("Working directory inside the container (defaults to /workspace)")]
    public string WorkingDirectory { get; set; }

    [Description("Timeout in seconds (default 30, max 300)")]
    public int? TimeoutSeconds { get; set; }
}

public class ExecScriptArgs
{
    [Required, Description("Organisation GUID")]
    public Guid? OrganizationId { get; set; }

    [Required, Description("The workspace ID")]
    public string WorkspaceId { get; set; }

    [Required, Description("The script source code")]
    public string ScriptContent { get; set; }

    [Required, Description("Script type: 'python' or 'node'")]
    public string ScriptType { get; set; }

    [Description("Timeout in seconds (default 30, max 300)")]
    public int? TimeoutSeconds { get; set; }
}

public class CopyBetweenWorkspacesArgs
{
    [Required, Description("Organisation GUID")]
    public Guid? OrganizationId { get; set; }

    [Required, Description("The source workspace ID")]
    public string SourceWorkspaceId { get; set; }

    [Required, Description("File path in the source workspace")]
    public string SourcePath { get; set; }

    [Required, Description("The destination workspace ID")]
    public string DestWorkspaceId { get; set; }

    [Required, Description("File path in the destination workspace")]
    public string DestPath { get; set; }
}
