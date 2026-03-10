using HQ.Models.Interfaces;

namespace HQ.Plugins.Jira.Models;

public record ServiceRequest : IPluginServiceRequest
{
    // IPluginServiceRequest
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Issue fields
    public string IssueKey { get; set; }
    public string ProjectKey { get; set; }
    public string Summary { get; set; }
    public string Description { get; set; }
    public string IssueType { get; set; }
    public string Priority { get; set; }
    public string AssigneeAccountId { get; set; }
    public string Labels { get; set; }
    public string ParentKey { get; set; }

    // Transition
    public string TransitionId { get; set; }

    // Comment
    public string CommentBody { get; set; }
    public string CommentId { get; set; }

    // Search
    public string Jql { get; set; }
    public int? MaxResults { get; set; }

    // Sprint/Board
    public int? BoardId { get; set; }
    public int? SprintId { get; set; }
    public string SprintName { get; set; }
    public string SprintGoal { get; set; }
    public string StartDate { get; set; }
    public string EndDate { get; set; }
    public string IssueKeys { get; set; }

    // Worklog
    public int? TimeSpentSeconds { get; set; }
    public string WorklogStarted { get; set; }
    public string WorklogComment { get; set; }

    // User
    public string Query { get; set; }

    // Link
    public string LinkType { get; set; }
    public string InwardIssueKey { get; set; }
    public string OutwardIssueKey { get; set; }

    // General
    public string Fields { get; set; }
    public string Expand { get; set; }
}
