namespace HQ.Plugins.Jira.Models;

public static class JiraMethods
{
    // Issues
    public const string SearchIssues = "jira_search_issues";
    public const string GetIssue = "jira_get_issue";
    public const string CreateIssue = "jira_create_issue";
    public const string UpdateIssue = "jira_update_issue";
    public const string DeleteIssue = "jira_delete_issue";
    public const string AssignIssue = "jira_assign_issue";
    public const string TransitionIssue = "jira_transition_issue";
    public const string AddComment = "jira_add_comment";
    public const string GetComments = "jira_get_comments";
    public const string LinkIssues = "jira_link_issues";

    // Projects
    public const string ListProjects = "jira_list_projects";
    public const string GetProject = "jira_get_project";

    // Boards & Sprints
    public const string ListBoards = "jira_list_boards";
    public const string GetSprint = "jira_get_sprint";
    public const string GetSprintIssues = "jira_get_sprint_issues";
    public const string MoveToSprint = "jira_move_to_sprint";

    // Users
    public const string SearchUsers = "jira_search_users";

    // Worklogs
    public const string AddWorklog = "jira_add_worklog";
    public const string GetWorklogs = "jira_get_worklogs";

    // Metadata
    public const string GetIssueTypes = "jira_get_issue_types";
    public const string GetStatuses = "jira_get_statuses";
}
