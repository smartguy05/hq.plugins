using HQ.Models.Interfaces;

namespace HQ.Plugins.Asana.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Task fields
    public string TaskId { get; set; }
    public string Name { get; set; }
    public string Notes { get; set; }
    public string HtmlNotes { get; set; }
    public string Assignee { get; set; }
    public string DueOn { get; set; }
    public string DueAt { get; set; }
    public string StartOn { get; set; }
    public bool? Completed { get; set; }
    public string Parent { get; set; }
    public string Followers { get; set; }
    public string CustomFields { get; set; }

    // Project fields
    public string ProjectId { get; set; }
    public string SectionId { get; set; }
    public bool? Archived { get; set; }

    // Workspace/org fields
    public string Workspace { get; set; }
    public string Team { get; set; }

    // User fields
    public string UserId { get; set; }

    // Search fields
    public string Text { get; set; }
    public string Query { get; set; }
    public string ResourceType { get; set; }
    public string AssigneeAny { get; set; }
    public string ProjectsAny { get; set; }
    public string DueOnBefore { get; set; }
    public string DueOnAfter { get; set; }
    public string SortBy { get; set; }
    public bool? SortAscending { get; set; }

    // Story/comment fields
    public string StoryText { get; set; }
    public string HtmlText { get; set; }

    // Pagination & options
    public int? Limit { get; set; }
    public string Offset { get; set; }
    public string OptFields { get; set; }
    public int? Count { get; set; }

    // Flags
    public bool? IncludeSubtasks { get; set; }
    public bool? IncludeComments { get; set; }
}
