using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace HQ.Plugins.Asana.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. GID fields that LLMs sometimes send unquoted keep the <see cref="StringOrNumberConverter"/>.
/// </summary>

/// <summary>Args for tools that take no LLM parameters.</summary>
public class EmptyArgs;

// ───────────────────────────── Workspaces ─────────────────────────────

public class ListWorkspacesArgs
{
    [Description("Max results per page (1-100)")]
    public int? Limit { get; set; }

    [Description("Comma-separated fields to include (e.g. name,is_organization)")]
    public string OptFields { get; set; }
}

// ───────────────────────────── Users ─────────────────────────────

public class GetUserArgs
{
    [Description("User GID or 'me' for the authenticated user (default: me)")]
    public string UserId { get; set; }

    [Description("Comma-separated fields to include")]
    public string OptFields { get; set; }
}

// ───────────────────────────── Tasks ─────────────────────────────

public class CreateTaskArgs
{
    [Required, Description("Task name/title")]
    public string Name { get; set; }

    [Description("Plain text description")]
    public string Notes { get; set; }

    [Description("Rich text description in HTML")]
    public string HtmlNotes { get; set; }

    [Description("Assignee: user GID, email, or 'me'")]
    public string Assignee { get; set; }

    [Description("Due date (YYYY-MM-DD)")]
    public string DueOn { get; set; }

    [Description("Due datetime (ISO 8601)")]
    public string DueAt { get; set; }

    [Description("Start date (YYYY-MM-DD)")]
    public string StartOn { get; set; }

    [Description("Whether the task is completed")]
    public bool? Completed { get; set; }

    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("Project GID to add the task to")]
    public string ProjectId { get; set; }

    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("Section GID to place the task in")]
    public string SectionId { get; set; }

    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("Parent task GID (creates a subtask)")]
    public string Parent { get; set; }

    [Description("Workspace GID (required if no project specified)")]
    public string Workspace { get; set; }

    [Description("Comma-separated user GIDs or emails to add as followers")]
    public string Followers { get; set; }

    [Description("JSON object mapping custom field GIDs to values")]
    public string CustomFields { get; set; }
}

public class GetTaskArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Required, Description("The task GID")]
    public string TaskId { get; set; }

    [Description("Comma-separated fields to include")]
    public string OptFields { get; set; }

    [Description("Also fetch subtasks (default: false)")]
    public bool? IncludeSubtasks { get; set; }

    [Description("Also fetch comments/stories (default: false)")]
    public bool? IncludeComments { get; set; }
}

public class UpdateTaskArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Required, Description("The task GID")]
    public string TaskId { get; set; }

    [Description("Updated task name")]
    public string Name { get; set; }

    [Description("Updated plain text description")]
    public string Notes { get; set; }

    [Description("Updated rich text description in HTML")]
    public string HtmlNotes { get; set; }

    [Description("Updated assignee: user GID, email, or 'me'")]
    public string Assignee { get; set; }

    [Description("Updated due date (YYYY-MM-DD)")]
    public string DueOn { get; set; }

    [Description("Updated due datetime (ISO 8601)")]
    public string DueAt { get; set; }

    [Description("Updated start date (YYYY-MM-DD)")]
    public string StartOn { get; set; }

    [Description("Mark task as completed or incomplete")]
    public bool? Completed { get; set; }
}

public class DeleteTaskArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Required, Description("The task GID to delete")]
    public string TaskId { get; set; }
}

public class SearchTasksArgs
{
    [Required, Description("Workspace GID (required)")]
    public string Workspace { get; set; }

    [Description("Full-text search query")]
    public string Text { get; set; }

    [Description("Comma-separated user GIDs to filter by assignee")]
    public string AssigneeAny { get; set; }

    [Description("Comma-separated project GIDs to filter by project")]
    public string ProjectsAny { get; set; }

    [Description("Filter by completion status")]
    public bool? Completed { get; set; }

    [Description("Tasks due before this date (YYYY-MM-DD)")]
    public string DueOnBefore { get; set; }

    [Description("Tasks due after this date (YYYY-MM-DD)")]
    public string DueOnAfter { get; set; }

    [Description("Sort field: due_date, created_at, completed_at, likes, modified_at (default: modified_at)")]
    public string SortBy { get; set; }

    [Description("Sort ascending (default: false)")]
    public bool? SortAscending { get; set; }

    [Description("Comma-separated fields to include")]
    public string OptFields { get; set; }

    [Description("Max results (1-100)")]
    public int? Limit { get; set; }
}

public class GetTasksArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("Project GID to list tasks from")]
    public string ProjectId { get; set; }

    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("Section GID to list tasks from")]
    public string SectionId { get; set; }

    [Description("User GID or 'me' to filter by assignee (requires workspace)")]
    public string Assignee { get; set; }

    [Description("Workspace GID (required when filtering by assignee)")]
    public string Workspace { get; set; }

    [Description("Filter by completion status")]
    public bool? Completed { get; set; }

    [Description("Comma-separated fields to include")]
    public string OptFields { get; set; }

    [Description("Max results per page (1-100)")]
    public int? Limit { get; set; }

    [Description("Pagination offset token")]
    public string Offset { get; set; }
}

public class SetParentForTaskArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Required, Description("The task GID to reparent")]
    public string TaskId { get; set; }

    [JsonConverter(typeof(StringOrNumberConverter))]
    [Required, Description("The parent task GID (or null to remove parent)")]
    public string Parent { get; set; }
}

public class AddTaskFollowersArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Required, Description("The task GID")]
    public string TaskId { get; set; }

    [Required, Description("Comma-separated user GIDs or emails to add as followers")]
    public string Followers { get; set; }
}

public class MoveTaskToSectionArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Required, Description("The task GID to move")]
    public string TaskId { get; set; }

    [JsonConverter(typeof(StringOrNumberConverter))]
    [Required, Description("The target section GID")]
    public string SectionId { get; set; }
}

// ───────────────────────────── Projects ─────────────────────────────

public class GetProjectsArgs
{
    [Required, Description("Workspace GID (required)")]
    public string Workspace { get; set; }

    [Description("Team GID to filter by")]
    public string Team { get; set; }

    [Description("Filter by archived status (default: false)")]
    public bool? Archived { get; set; }

    [Description("Comma-separated fields to include")]
    public string OptFields { get; set; }

    [Description("Max results per page (1-100)")]
    public int? Limit { get; set; }

    [Description("Pagination offset token")]
    public string Offset { get; set; }
}

public class GetProjectArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Required, Description("The project GID")]
    public string ProjectId { get; set; }

    [Description("Comma-separated fields to include")]
    public string OptFields { get; set; }
}

public class GetProjectSectionsArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Required, Description("The project GID")]
    public string ProjectId { get; set; }

    [Description("Comma-separated fields to include")]
    public string OptFields { get; set; }

    [Description("Max results per page (1-100)")]
    public int? Limit { get; set; }
}

// ───────────────────────────── Stories / Comments ─────────────────────────────

public class CreateTaskStoryArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Required, Description("The task GID to comment on")]
    public string TaskId { get; set; }

    [Required, Description("Plain text comment")]
    public string StoryText { get; set; }

    [Description("Rich text comment in HTML")]
    public string HtmlText { get; set; }
}

public class GetStoriesForTaskArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Required, Description("The task GID")]
    public string TaskId { get; set; }

    [Description("Comma-separated fields to include")]
    public string OptFields { get; set; }

    [Description("Max results per page (1-100)")]
    public int? Limit { get; set; }
}

// ───────────────────────────── Search ─────────────────────────────

public class TypeaheadSearchArgs
{
    [Required, Description("Workspace GID (required)")]
    public string Workspace { get; set; }

    [Required, Description("Search query string")]
    public string Query { get; set; }

    [Description("Type to search: task, project, user, tag, portfolio (default: task)")]
    public string ResourceType { get; set; }

    [Description("Max results (1-100, default: 20)")]
    public int? Count { get; set; }

    [Description("Comma-separated fields to include")]
    public string OptFields { get; set; }
}
