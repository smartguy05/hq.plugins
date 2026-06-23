using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Jira.Models;

namespace HQ.Plugins.Jira;

public class JiraService
{
    private readonly JiraClient _client;
    private readonly LogDelegate _logger;

    public JiraService(ServiceConfig config, LogDelegate logger)
    {
        _logger = logger;
        _client = new JiraClient(config.Domain, config.Email, config.ApiToken);
    }

    // ───────────────────────────── Issues ─────────────────────────────

    [Display(Name = "jira_search_issues")]
    [Description("Search for Jira issues using JQL (Jira Query Language). Returns a summary of matching issues including key, summary, status, assignee, and priority.")]
    [Parameters(typeof(SearchIssuesArgs))]
    public async Task<object> SearchIssues(ServiceConfig config, SearchIssuesArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.Jql))
            throw new ArgumentException("Missing required parameter: jql");

        var maxResults = request.MaxResults ?? 50;
        if (maxResults > 100) maxResults = 100;

        var result = await _client.PostAsync("search/jql", new
        {
            jql = request.Jql,
            maxResults,
            fields = new[] { "summary", "status", "assignee", "priority", "issuetype", "created", "updated" }
        });

        var issues = new List<object>();
        if (result.TryGetProperty("issues", out var issuesArray))
        {
            foreach (var issue in issuesArray.EnumerateArray())
            {
                var fields = issue.GetProperty("fields");
                issues.Add(new
                {
                    Key = issue.GetProperty("key").GetString(),
                    Summary = GetStringOrNull(fields, "summary"),
                    Status = GetNestedName(fields, "status"),
                    Assignee = GetNestedDisplayName(fields, "assignee"),
                    Priority = GetNestedName(fields, "priority"),
                    IssueType = GetNestedName(fields, "issuetype"),
                    Created = GetStringOrNull(fields, "created"),
                    Updated = GetStringOrNull(fields, "updated")
                });
            }
        }

        var total = result.TryGetProperty("total", out var totalProp) ? totalProp.GetInt32() : issues.Count;

        return new { Total = total, Issues = issues };
    }

    [Display(Name = "jira_get_issue")]
    [Description("Get full details of a Jira issue by its key (e.g. PROJ-123). Returns all fields including description (converted from ADF to plain text), status, assignee, comments count, and subtasks.")]
    [Parameters(typeof(GetIssueArgs))]
    public async Task<object> GetIssue(ServiceConfig config, GetIssueArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.IssueKey))
            throw new ArgumentException("Missing required parameter: issueKey");

        var path = $"issue/{request.IssueKey}";
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Fields))
            queryParams.Add($"fields={request.Fields}");
        if (!string.IsNullOrWhiteSpace(request.Expand))
            queryParams.Add($"expand={request.Expand}");
        if (queryParams.Count > 0)
            path += "?" + string.Join("&", queryParams);

        var result = await _client.GetAsync(path);
        var fields = result.GetProperty("fields");

        var description = string.Empty;
        if (fields.TryGetProperty("description", out var descProp) && descProp.ValueKind != JsonValueKind.Null)
            description = JiraClient.FromAdf(descProp);

        var labels = new List<string>();
        if (fields.TryGetProperty("labels", out var labelsProp) && labelsProp.ValueKind == JsonValueKind.Array)
            labels = labelsProp.EnumerateArray().Select(l => l.GetString()).ToList();

        var subtasks = new List<object>();
        if (fields.TryGetProperty("subtasks", out var subtasksProp) && subtasksProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var st in subtasksProp.EnumerateArray())
            {
                var stFields = st.GetProperty("fields");
                subtasks.Add(new
                {
                    Key = st.GetProperty("key").GetString(),
                    Summary = GetStringOrNull(stFields, "summary"),
                    Status = GetNestedName(stFields, "status")
                });
            }
        }

        return new
        {
            Key = result.GetProperty("key").GetString(),
            Summary = GetStringOrNull(fields, "summary"),
            Description = description,
            Status = GetNestedName(fields, "status"),
            Priority = GetNestedName(fields, "priority"),
            IssueType = GetNestedName(fields, "issuetype"),
            Assignee = GetNestedDisplayName(fields, "assignee"),
            Reporter = GetNestedDisplayName(fields, "reporter"),
            Labels = labels,
            Created = GetStringOrNull(fields, "created"),
            Updated = GetStringOrNull(fields, "updated"),
            Parent = fields.TryGetProperty("parent", out var parentProp) && parentProp.ValueKind != JsonValueKind.Null
                ? parentProp.GetProperty("key").GetString()
                : null,
            Subtasks = subtasks
        };
    }

    [Display(Name = "jira_create_issue")]
    [Description("Create a new Jira issue. Requires project key, issue type, and summary. Description is converted to Atlassian Document Format automatically.")]
    [Parameters(typeof(CreateIssueArgs))]
    public async Task<object> CreateIssue(ServiceConfig config, CreateIssueArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectKey))
            throw new ArgumentException("Missing required parameter: projectKey");
        if (string.IsNullOrWhiteSpace(request.IssueType))
            throw new ArgumentException("Missing required parameter: issueType");
        if (string.IsNullOrWhiteSpace(request.Summary))
            throw new ArgumentException("Missing required parameter: summary");

        var fields = new Dictionary<string, object>
        {
            ["project"] = new { key = request.ProjectKey },
            ["issuetype"] = new { name = request.IssueType },
            ["summary"] = request.Summary
        };

        if (!string.IsNullOrWhiteSpace(request.Description))
            fields["description"] = JiraClient.ToAdf(request.Description);

        if (!string.IsNullOrWhiteSpace(request.Priority))
            fields["priority"] = new { name = request.Priority };

        if (!string.IsNullOrWhiteSpace(request.AssigneeAccountId))
            fields["assignee"] = new { accountId = request.AssigneeAccountId };

        if (!string.IsNullOrWhiteSpace(request.Labels))
            fields["labels"] = request.Labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (!string.IsNullOrWhiteSpace(request.ParentKey))
            fields["parent"] = new { key = request.ParentKey };

        var result = await _client.PostAsync("issue", new { fields });

        return new
        {
            Success = true,
            Key = result.GetProperty("key").GetString(),
            Id = result.GetProperty("id").GetString(),
            Self = result.GetProperty("self").GetString()
        };
    }

    [Display(Name = "jira_update_issue")]
    [Description("Update fields on an existing Jira issue. Only non-null fields are updated. Description is converted to ADF automatically.")]
    [Parameters(typeof(UpdateIssueArgs))]
    public async Task<object> UpdateIssue(ServiceConfig config, UpdateIssueArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.IssueKey))
            throw new ArgumentException("Missing required parameter: issueKey");

        var fields = new Dictionary<string, object>();

        if (request.Summary != null)
            fields["summary"] = request.Summary;

        if (request.Description != null)
            fields["description"] = JiraClient.ToAdf(request.Description);

        if (request.Priority != null)
            fields["priority"] = new { name = request.Priority };

        if (request.AssigneeAccountId != null)
            fields["assignee"] = string.IsNullOrEmpty(request.AssigneeAccountId)
                ? null
                : new { accountId = request.AssigneeAccountId };

        if (request.Labels != null)
            fields["labels"] = request.Labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (fields.Count == 0)
            return new { Success = false, Message = "No fields to update" };

        await _client.PutAsync($"issue/{request.IssueKey}", new { fields });

        return new { Success = true, Message = $"Issue {request.IssueKey} updated" };
    }

    [Display(Name = "jira_delete_issue")]
    [Description("Delete a Jira issue by its key. Also deletes subtasks.")]
    [Parameters(typeof(DeleteIssueArgs))]
    public async Task<object> DeleteIssue(ServiceConfig config, DeleteIssueArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.IssueKey))
            throw new ArgumentException("Missing required parameter: issueKey");

        await _client.DeleteAsync($"issue/{request.IssueKey}?deleteSubtasks=true");

        return new { Success = true, Message = $"Issue {request.IssueKey} deleted" };
    }

    [Display(Name = "jira_assign_issue")]
    [Description("Assign or unassign a Jira issue. Pass an accountId to assign, or omit/empty to unassign.")]
    [Parameters(typeof(AssignIssueArgs))]
    public async Task<object> AssignIssue(ServiceConfig config, AssignIssueArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.IssueKey))
            throw new ArgumentException("Missing required parameter: issueKey");

        var accountId = string.IsNullOrWhiteSpace(request.AssigneeAccountId)
            ? null
            : request.AssigneeAccountId;

        await _client.PutAsync($"issue/{request.IssueKey}/assignee", new { accountId });

        var action = accountId == null ? "unassigned" : $"assigned to {accountId}";
        return new { Success = true, Message = $"Issue {request.IssueKey} {action}" };
    }

    [Display(Name = "jira_transition_issue")]
    [Description("Transition a Jira issue to a new status. If transitionId is not provided, returns the list of available transitions so you can pick one. If provided, executes the transition.")]
    [Parameters(typeof(TransitionIssueArgs))]
    public async Task<object> TransitionIssue(ServiceConfig config, TransitionIssueArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.IssueKey))
            throw new ArgumentException("Missing required parameter: issueKey");

        if (string.IsNullOrWhiteSpace(request.TransitionId))
        {
            // List available transitions
            var result = await _client.GetAsync($"issue/{request.IssueKey}/transitions");
            var transitions = new List<object>();

            if (result.TryGetProperty("transitions", out var transArray))
            {
                foreach (var t in transArray.EnumerateArray())
                {
                    transitions.Add(new
                    {
                        Id = t.GetProperty("id").GetString(),
                        Name = t.GetProperty("name").GetString(),
                        ToStatus = t.TryGetProperty("to", out var toProp)
                            ? GetStringOrNull(toProp, "name")
                            : null
                    });
                }
            }

            return new { IssueKey = request.IssueKey, AvailableTransitions = transitions };
        }

        await _client.PostAsync($"issue/{request.IssueKey}/transitions", new
        {
            transition = new { id = request.TransitionId }
        });

        return new { Success = true, Message = $"Issue {request.IssueKey} transitioned" };
    }

    [Display(Name = "jira_add_comment")]
    [Description("Add a comment to a Jira issue. The comment body is plain text and will be converted to ADF automatically.")]
    [Parameters(typeof(AddCommentArgs))]
    public async Task<object> AddComment(ServiceConfig config, AddCommentArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.IssueKey))
            throw new ArgumentException("Missing required parameter: issueKey");
        if (string.IsNullOrWhiteSpace(request.CommentBody))
            throw new ArgumentException("Missing required parameter: commentBody");

        var result = await _client.PostAsync($"issue/{request.IssueKey}/comment", new
        {
            body = JiraClient.ToAdf(request.CommentBody)
        });

        return new
        {
            Success = true,
            CommentId = result.GetProperty("id").GetString(),
            IssueKey = request.IssueKey
        };
    }

    [Display(Name = "jira_get_comments")]
    [Description("Get comments on a Jira issue. Comment bodies are converted from ADF to plain text.")]
    [Parameters(typeof(GetCommentsArgs))]
    public async Task<object> GetComments(ServiceConfig config, GetCommentsArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.IssueKey))
            throw new ArgumentException("Missing required parameter: issueKey");

        var maxResults = request.MaxResults ?? 50;
        var result = await _client.GetAsync($"issue/{request.IssueKey}/comment?maxResults={maxResults}&orderBy=-created");

        var comments = new List<object>();
        if (result.TryGetProperty("comments", out var commentsArray))
        {
            foreach (var c in commentsArray.EnumerateArray())
            {
                var body = c.TryGetProperty("body", out var bodyProp) ? JiraClient.FromAdf(bodyProp) : string.Empty;
                comments.Add(new
                {
                    Id = c.GetProperty("id").GetString(),
                    Author = c.TryGetProperty("author", out var authorProp)
                        ? GetStringOrNull(authorProp, "displayName")
                        : null,
                    Body = body,
                    Created = GetStringOrNull(c, "created"),
                    Updated = GetStringOrNull(c, "updated")
                });
            }
        }

        var total = result.TryGetProperty("total", out var totalProp) ? totalProp.GetInt32() : comments.Count;

        return new { Total = total, Comments = comments };
    }

    [Display(Name = "jira_link_issues")]
    [Description("Create a link between two Jira issues with a specified relationship type (e.g. Blocks, Relates, Duplicate, Cloners).")]
    [Parameters(typeof(LinkIssuesArgs))]
    public async Task<object> LinkIssues(ServiceConfig config, LinkIssuesArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.LinkType))
            throw new ArgumentException("Missing required parameter: linkType");
        if (string.IsNullOrWhiteSpace(request.InwardIssueKey))
            throw new ArgumentException("Missing required parameter: inwardIssueKey");
        if (string.IsNullOrWhiteSpace(request.OutwardIssueKey))
            throw new ArgumentException("Missing required parameter: outwardIssueKey");

        await _client.PostAsync("issueLink", new
        {
            type = new { name = request.LinkType },
            inwardIssue = new { key = request.InwardIssueKey },
            outwardIssue = new { key = request.OutwardIssueKey }
        });

        return new
        {
            Success = true,
            Message = $"Linked {request.InwardIssueKey} <-[{request.LinkType}]-> {request.OutwardIssueKey}"
        };
    }

    // ───────────────────────────── Projects ─────────────────────────────

    [Display(Name = "jira_list_projects")]
    [Description("List Jira projects accessible to the authenticated user. Returns project key, name, type, and lead.")]
    [Parameters(typeof(ListProjectsArgs))]
    public async Task<object> ListProjects(ServiceConfig config, ListProjectsArgs request)
    {
        var maxResults = request.MaxResults ?? 50;
        var result = await _client.GetAsync($"project/search?maxResults={maxResults}");

        var projects = new List<object>();
        if (result.TryGetProperty("values", out var valuesArray))
        {
            foreach (var p in valuesArray.EnumerateArray())
            {
                projects.Add(new
                {
                    Key = p.GetProperty("key").GetString(),
                    Name = GetStringOrNull(p, "name"),
                    ProjectType = GetStringOrNull(p, "projectTypeKey"),
                    Lead = p.TryGetProperty("lead", out var leadProp)
                        ? GetStringOrNull(leadProp, "displayName")
                        : null
                });
            }
        }

        return new { Projects = projects };
    }

    [Display(Name = "jira_get_project")]
    [Description("Get details of a Jira project by its key, including issue types, components, and project lead.")]
    [Parameters(typeof(GetProjectArgs))]
    public async Task<object> GetProject(ServiceConfig config, GetProjectArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectKey))
            throw new ArgumentException("Missing required parameter: projectKey");

        var result = await _client.GetAsync($"project/{request.ProjectKey}");

        var issueTypes = new List<object>();
        if (result.TryGetProperty("issueTypes", out var itArray))
        {
            foreach (var it in itArray.EnumerateArray())
            {
                issueTypes.Add(new
                {
                    Name = GetStringOrNull(it, "name"),
                    Description = GetStringOrNull(it, "description"),
                    Subtask = it.TryGetProperty("subtask", out var stProp) && stProp.GetBoolean()
                });
            }
        }

        var components = new List<object>();
        if (result.TryGetProperty("components", out var compArray))
        {
            foreach (var comp in compArray.EnumerateArray())
            {
                components.Add(new
                {
                    Name = GetStringOrNull(comp, "name"),
                    Description = GetStringOrNull(comp, "description")
                });
            }
        }

        return new
        {
            Key = result.GetProperty("key").GetString(),
            Name = GetStringOrNull(result, "name"),
            Description = GetStringOrNull(result, "description"),
            Lead = result.TryGetProperty("lead", out var leadProp)
                ? GetStringOrNull(leadProp, "displayName")
                : null,
            ProjectType = GetStringOrNull(result, "projectTypeKey"),
            IssueTypes = issueTypes,
            Components = components
        };
    }

    // ───────────────────────────── Boards & Sprints ─────────────────────────────

    [Display(Name = "jira_list_boards")]
    [Description("List Jira boards, optionally filtered by project key. Uses the Agile REST API.")]
    [Parameters(typeof(ListBoardsArgs))]
    public async Task<object> ListBoards(ServiceConfig config, ListBoardsArgs request)
    {
        var maxResults = request.MaxResults ?? 50;
        var path = $"board?maxResults={maxResults}";
        if (!string.IsNullOrWhiteSpace(request.ProjectKey))
            path += $"&projectKeyOrId={request.ProjectKey}";

        var result = await _client.GetAgileAsync(path);

        var boards = new List<object>();
        if (result.TryGetProperty("values", out var valuesArray))
        {
            foreach (var b in valuesArray.EnumerateArray())
            {
                boards.Add(new
                {
                    Id = b.GetProperty("id").GetInt32(),
                    Name = GetStringOrNull(b, "name"),
                    Type = GetStringOrNull(b, "type")
                });
            }
        }

        return new { Boards = boards };
    }

    [Display(Name = "jira_get_sprint")]
    [Description("Get sprints for a board. By default returns active sprints. Specify state to filter (active, future, closed).")]
    [Parameters(typeof(GetSprintArgs))]
    public async Task<object> GetSprint(ServiceConfig config, GetSprintArgs request)
    {
        if (request.BoardId == null)
            throw new ArgumentException("Missing required parameter: boardId");

        var state = string.IsNullOrWhiteSpace(request.SprintName) ? "active" : request.SprintName;
        var result = await _client.GetAgileAsync($"board/{request.BoardId}/sprint?state={state}");

        var sprints = new List<object>();
        if (result.TryGetProperty("values", out var valuesArray))
        {
            foreach (var s in valuesArray.EnumerateArray())
            {
                sprints.Add(new
                {
                    Id = s.GetProperty("id").GetInt32(),
                    Name = GetStringOrNull(s, "name"),
                    State = GetStringOrNull(s, "state"),
                    Goal = GetStringOrNull(s, "goal"),
                    StartDate = GetStringOrNull(s, "startDate"),
                    EndDate = GetStringOrNull(s, "endDate")
                });
            }
        }

        return new { Sprints = sprints };
    }

    [Display(Name = "jira_get_sprint_issues")]
    [Description("Get issues in a specific sprint, ordered by rank.")]
    [Parameters(typeof(GetSprintIssuesArgs))]
    public async Task<object> GetSprintIssues(ServiceConfig config, GetSprintIssuesArgs request)
    {
        if (request.SprintId == null)
            throw new ArgumentException("Missing required parameter: sprintId");

        var maxResults = request.MaxResults ?? 50;
        var result = await _client.GetAgileAsync($"sprint/{request.SprintId}/issue?maxResults={maxResults}");

        var issues = new List<object>();
        if (result.TryGetProperty("issues", out var issuesArray))
        {
            foreach (var issue in issuesArray.EnumerateArray())
            {
                var fields = issue.GetProperty("fields");
                issues.Add(new
                {
                    Key = issue.GetProperty("key").GetString(),
                    Summary = GetStringOrNull(fields, "summary"),
                    Status = GetNestedName(fields, "status"),
                    Assignee = GetNestedDisplayName(fields, "assignee"),
                    Priority = GetNestedName(fields, "priority"),
                    IssueType = GetNestedName(fields, "issuetype")
                });
            }
        }

        var total = result.TryGetProperty("total", out var totalProp) ? totalProp.GetInt32() : issues.Count;

        return new { Total = total, Issues = issues };
    }

    [Display(Name = "jira_move_to_sprint")]
    [Description("Move one or more issues into a sprint. Maximum 50 issues per call.")]
    [Parameters(typeof(MoveToSprintArgs))]
    public async Task<object> MoveToSprint(ServiceConfig config, MoveToSprintArgs request)
    {
        if (request.SprintId == null)
            throw new ArgumentException("Missing required parameter: sprintId");
        if (string.IsNullOrWhiteSpace(request.IssueKeys))
            throw new ArgumentException("Missing required parameter: issueKeys");

        var keys = request.IssueKeys.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (keys.Length > 50)
            throw new ArgumentException("Maximum 50 issues per call");

        await _client.PostAgileAsync($"sprint/{request.SprintId}/issue", new
        {
            issues = keys
        });

        return new { Success = true, Message = $"Moved {keys.Length} issue(s) to sprint {request.SprintId}" };
    }

    // ───────────────────────────── Users ─────────────────────────────

    [Display(Name = "jira_search_users")]
    [Description("Search for Jira users by name or email. Optionally filter to users assignable to a specific project.")]
    [Parameters(typeof(SearchUsersArgs))]
    public async Task<object> SearchUsers(ServiceConfig config, SearchUsersArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Missing required parameter: query");

        JsonElement result;
        if (!string.IsNullOrWhiteSpace(request.ProjectKey))
        {
            result = await _client.GetAsync(
                $"user/assignable/search?query={Uri.EscapeDataString(request.Query)}&project={request.ProjectKey}&maxResults=25");
        }
        else
        {
            result = await _client.GetAsync(
                $"user/search?query={Uri.EscapeDataString(request.Query)}&maxResults=25");
        }

        var users = new List<object>();
        if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (var u in result.EnumerateArray())
            {
                users.Add(new
                {
                    AccountId = GetStringOrNull(u, "accountId"),
                    DisplayName = GetStringOrNull(u, "displayName"),
                    EmailAddress = GetStringOrNull(u, "emailAddress"),
                    Active = u.TryGetProperty("active", out var activeProp) && activeProp.GetBoolean()
                });
            }
        }

        return new { Users = users };
    }

    // ───────────────────────────── Worklogs ─────────────────────────────

    [Display(Name = "jira_add_worklog")]
    [Description("Log time spent on a Jira issue. Takes time in seconds, a start datetime, and an optional comment.")]
    [Parameters(typeof(AddWorklogArgs))]
    public async Task<object> AddWorklog(ServiceConfig config, AddWorklogArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.IssueKey))
            throw new ArgumentException("Missing required parameter: issueKey");
        if (request.TimeSpentSeconds == null)
            throw new ArgumentException("Missing required parameter: timeSpentSeconds");
        if (string.IsNullOrWhiteSpace(request.WorklogStarted))
            throw new ArgumentException("Missing required parameter: worklogStarted");

        var body = new Dictionary<string, object>
        {
            ["timeSpentSeconds"] = request.TimeSpentSeconds.Value,
            ["started"] = request.WorklogStarted
        };

        if (!string.IsNullOrWhiteSpace(request.WorklogComment))
            body["comment"] = JiraClient.ToAdf(request.WorklogComment);

        var result = await _client.PostAsync($"issue/{request.IssueKey}/worklog", body);

        return new
        {
            Success = true,
            WorklogId = result.GetProperty("id").GetString(),
            IssueKey = request.IssueKey,
            TimeSpentSeconds = request.TimeSpentSeconds.Value
        };
    }

    [Display(Name = "jira_get_worklogs")]
    [Description("Get worklogs (time entries) for a Jira issue.")]
    [Parameters(typeof(GetWorklogsArgs))]
    public async Task<object> GetWorklogs(ServiceConfig config, GetWorklogsArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.IssueKey))
            throw new ArgumentException("Missing required parameter: issueKey");

        var result = await _client.GetAsync($"issue/{request.IssueKey}/worklog");

        var worklogs = new List<object>();
        if (result.TryGetProperty("worklogs", out var worklogsArray))
        {
            foreach (var w in worklogsArray.EnumerateArray())
            {
                var comment = w.TryGetProperty("comment", out var commentProp)
                    ? JiraClient.FromAdf(commentProp)
                    : string.Empty;

                worklogs.Add(new
                {
                    Id = w.GetProperty("id").GetString(),
                    Author = w.TryGetProperty("author", out var authorProp)
                        ? GetStringOrNull(authorProp, "displayName")
                        : null,
                    TimeSpentSeconds = w.TryGetProperty("timeSpentSeconds", out var tsProp) ? tsProp.GetInt32() : 0,
                    TimeSpent = GetStringOrNull(w, "timeSpent"),
                    Started = GetStringOrNull(w, "started"),
                    Comment = comment
                });
            }
        }

        var total = result.TryGetProperty("total", out var totalProp) ? totalProp.GetInt32() : worklogs.Count;

        return new { Total = total, Worklogs = worklogs };
    }

    // ───────────────────────────── Metadata ─────────────────────────────

    [Display(Name = "jira_get_issue_types")]
    [Description("Get the issue types available for a project (useful before creating an issue to know valid types).")]
    [Parameters(typeof(GetIssueTypesArgs))]
    public async Task<object> GetIssueTypes(ServiceConfig config, GetIssueTypesArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectKey))
            throw new ArgumentException("Missing required parameter: projectKey");

        var result = await _client.GetAsync($"issue/createmeta/{request.ProjectKey}/issuetypes");

        var issueTypes = new List<object>();
        if (result.TryGetProperty("issueTypes", out var itArray) || result.TryGetProperty("values", out itArray))
        {
            foreach (var it in itArray.EnumerateArray())
            {
                issueTypes.Add(new
                {
                    Id = GetStringOrNull(it, "id"),
                    Name = GetStringOrNull(it, "name"),
                    Description = GetStringOrNull(it, "description"),
                    Subtask = it.TryGetProperty("subtask", out var stProp) && stProp.GetBoolean()
                });
            }
        }

        return new { ProjectKey = request.ProjectKey, IssueTypes = issueTypes };
    }

    [Display(Name = "jira_get_statuses")]
    [Description("Get the available statuses for a project, grouped by issue type. Useful for understanding workflow states.")]
    [Parameters(typeof(GetStatusesArgs))]
    public async Task<object> GetStatuses(ServiceConfig config, GetStatusesArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectKey))
            throw new ArgumentException("Missing required parameter: projectKey");

        var result = await _client.GetAsync($"project/{request.ProjectKey}/statuses");

        var statusesByType = new List<object>();
        if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (var issueType in result.EnumerateArray())
            {
                var statuses = new List<object>();
                if (issueType.TryGetProperty("statuses", out var statusArray))
                {
                    foreach (var s in statusArray.EnumerateArray())
                    {
                        statuses.Add(new
                        {
                            Id = GetStringOrNull(s, "id"),
                            Name = GetStringOrNull(s, "name"),
                            Category = s.TryGetProperty("statusCategory", out var catProp)
                                ? GetStringOrNull(catProp, "name")
                                : null
                        });
                    }
                }

                statusesByType.Add(new
                {
                    IssueType = GetStringOrNull(issueType, "name"),
                    Statuses = statuses
                });
            }
        }

        return new { ProjectKey = request.ProjectKey, StatusesByType = statusesByType };
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private static string GetStringOrNull(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static string GetNestedName(JsonElement fields, string property)
    {
        if (!fields.TryGetProperty(property, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return null;
        return prop.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
    }

    private static string GetNestedDisplayName(JsonElement fields, string property)
    {
        if (!fields.TryGetProperty(property, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return null;
        return prop.TryGetProperty("displayName", out var nameProp) ? nameProp.GetString() : null;
    }
}
