using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.Jira.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM (e.g. <c>MaxResults</c> → <c>maxResults</c>); binding is case-insensitive. Fields a tool
/// body reads but does not advertise to the model are marked <c>[Injected]</c>.
/// </summary>

public class SearchIssuesArgs
{
    [Required, Description("JQL query string, e.g. 'project = PROJ AND status = \"In Progress\"'")]
    public string Jql { get; set; }

    [Description("Maximum number of results to return (default 50, max 100)")]
    public int? MaxResults { get; set; }
}

public class GetIssueArgs
{
    [Required, Description("The issue key, e.g. PROJ-123")]
    public string IssueKey { get; set; }

    [Description("Comma-separated list of fields to return (optional, returns all by default)")]
    public string Fields { get; set; }

    [Description("Comma-separated list of expansions (e.g. 'changelog,transitions')")]
    public string Expand { get; set; }
}

public class CreateIssueArgs
{
    [Required, Description("The project key, e.g. PROJ")]
    public string ProjectKey { get; set; }

    [Required, Description("Issue type name, e.g. Task, Bug, Story, Epic")]
    public string IssueType { get; set; }

    [Required, Description("Issue title/summary")]
    public string Summary { get; set; }

    [Description("Issue description (plain text, converted to ADF automatically)")]
    public string Description { get; set; }

    [Description("Priority name, e.g. High, Medium, Low")]
    public string Priority { get; set; }

    [Description("Atlassian account ID of the assignee")]
    public string AssigneeAccountId { get; set; }

    [Description("Comma-separated labels to apply")]
    public string Labels { get; set; }

    [Description("Parent issue key for subtasks or child issues")]
    public string ParentKey { get; set; }
}

public class UpdateIssueArgs
{
    [Required, Description("The issue key, e.g. PROJ-123")]
    public string IssueKey { get; set; }

    [Description("New summary/title")]
    public string Summary { get; set; }

    [Description("New description (plain text, converted to ADF)")]
    public string Description { get; set; }

    [Description("New priority name")]
    public string Priority { get; set; }

    [Description("New assignee account ID (use empty string to unassign)")]
    public string AssigneeAccountId { get; set; }

    [Description("Comma-separated labels (replaces existing labels)")]
    public string Labels { get; set; }
}

public class DeleteIssueArgs
{
    [Required, Description("The issue key to delete, e.g. PROJ-123")]
    public string IssueKey { get; set; }
}

public class AssignIssueArgs
{
    [Required, Description("The issue key, e.g. PROJ-123")]
    public string IssueKey { get; set; }

    [Description("Atlassian account ID to assign to, or empty/null to unassign")]
    public string AssigneeAccountId { get; set; }
}

public class TransitionIssueArgs
{
    [Required, Description("The issue key, e.g. PROJ-123")]
    public string IssueKey { get; set; }

    [Description("The transition ID to execute. Omit to list available transitions.")]
    public string TransitionId { get; set; }
}

public class AddCommentArgs
{
    [Required, Description("The issue key, e.g. PROJ-123")]
    public string IssueKey { get; set; }

    [Required, Description("The comment text")]
    public string CommentBody { get; set; }
}

public class GetCommentsArgs
{
    [Required, Description("The issue key, e.g. PROJ-123")]
    public string IssueKey { get; set; }

    [Description("Maximum number of comments to return (default 50)")]
    public int? MaxResults { get; set; }
}

public class LinkIssuesArgs
{
    [Required, Description("The link type name, e.g. 'Blocks', 'Relates', 'Duplicate', 'Cloners'")]
    public string LinkType { get; set; }

    [Required, Description("The inward issue key (e.g. the issue that 'is blocked by')")]
    public string InwardIssueKey { get; set; }

    [Required, Description("The outward issue key (e.g. the issue that 'blocks')")]
    public string OutwardIssueKey { get; set; }
}

public class ListProjectsArgs
{
    [Description("Maximum number of projects to return (default 50)")]
    public int? MaxResults { get; set; }
}

public class GetProjectArgs
{
    [Required, Description("The project key, e.g. PROJ")]
    public string ProjectKey { get; set; }
}

public class ListBoardsArgs
{
    [Description("Optional project key to filter boards")]
    public string ProjectKey { get; set; }

    [Description("Maximum number of boards to return (default 50)")]
    public int? MaxResults { get; set; }
}

public class GetSprintArgs
{
    [Required, Description("The board ID")]
    public int? BoardId { get; set; }

    [Description("Optional filter: state of sprints to return (active, future, closed). Defaults to 'active'.")]
    public string SprintName { get; set; }
}

public class GetSprintIssuesArgs
{
    [Required, Description("The sprint ID")]
    public int? SprintId { get; set; }

    [Description("Maximum number of issues to return (default 50)")]
    public int? MaxResults { get; set; }
}

public class MoveToSprintArgs
{
    [Required, Description("The target sprint ID")]
    public int? SprintId { get; set; }

    [Required, Description("Comma-separated issue keys to move, e.g. 'PROJ-1,PROJ-2,PROJ-3'")]
    public string IssueKeys { get; set; }
}

public class SearchUsersArgs
{
    [Required, Description("Search string (name or email)")]
    public string Query { get; set; }

    [Description("Optional project key to filter to assignable users")]
    public string ProjectKey { get; set; }
}

public class AddWorklogArgs
{
    [Required, Description("The issue key, e.g. PROJ-123")]
    public string IssueKey { get; set; }

    [Required, Description("Time spent in seconds (e.g. 3600 for 1 hour)")]
    public int? TimeSpentSeconds { get; set; }

    [Required, Description("When the work started in ISO 8601 format, e.g. 2024-01-15T09:00:00.000+0000")]
    public string WorklogStarted { get; set; }

    [Description("Optional comment describing the work done")]
    public string WorklogComment { get; set; }
}

public class GetWorklogsArgs
{
    [Required, Description("The issue key, e.g. PROJ-123")]
    public string IssueKey { get; set; }
}

public class GetIssueTypesArgs
{
    [Required, Description("The project key, e.g. PROJ")]
    public string ProjectKey { get; set; }
}

public class GetStatusesArgs
{
    [Required, Description("The project key, e.g. PROJ")]
    public string ProjectKey { get; set; }
}
