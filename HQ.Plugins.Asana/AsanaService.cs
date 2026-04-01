using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Asana.Models;

namespace HQ.Plugins.Asana;

public class AsanaService
{
    private readonly AsanaClient _client;
    private readonly LogDelegate _logger;

    public AsanaService(ServiceConfig config, LogDelegate logger)
    {
        _logger = logger;
        _client = new AsanaClient(config.BaseUrl, config.AccessToken);
    }

    // ───────────────────────────── Workspaces ─────────────────────────────

    [Display(Name = "list_workspaces")]
    [Description("List all Asana workspaces accessible to the authenticated user.")]
    [Parameters("""{"type":"object","properties":{"limit":{"type":"integer","description":"Max results per page (1-100)"},"optFields":{"type":"string","description":"Comma-separated fields to include (e.g. name,is_organization)"}},"required":[]}""")]
    public async Task<object> ListWorkspaces(ServiceConfig config, ServiceRequest request)
    {
        var query = BuildQuery(
            ("limit", request.Limit?.ToString()),
            ("opt_fields", request.OptFields ?? "name,is_organization"));

        var result = await _client.GetAsync($"/workspaces{query}");

        var workspaces = new List<object>();
        foreach (var ws in result.EnumerateArray())
        {
            workspaces.Add(new
            {
                Gid = GetProp(ws, "gid"),
                Name = GetProp(ws, "name"),
                IsOrganization = ws.TryGetProperty("is_organization", out var isOrg) && isOrg.GetBoolean()
            });
        }

        return new { Workspaces = workspaces };
    }

    // ───────────────────────────── Users ─────────────────────────────

    [Display(Name = "get_user")]
    [Description("Get details about an Asana user. Defaults to the authenticated user ('me').")]
    [Parameters("""{"type":"object","properties":{"userId":{"type":"string","description":"User GID or 'me' for the authenticated user (default: me)"},"optFields":{"type":"string","description":"Comma-separated fields to include"}},"required":[]}""")]
    public async Task<object> GetUser(ServiceConfig config, ServiceRequest request)
    {
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? "me" : request.UserId;
        var query = BuildQuery(("opt_fields", request.OptFields ?? "name,email,workspaces,workspaces.name"));

        var result = await _client.GetAsync($"/users/{userId}{query}");

        var workspaces = new List<object>();
        if (result.TryGetProperty("workspaces", out var wsList))
        {
            foreach (var ws in wsList.EnumerateArray())
            {
                workspaces.Add(new
                {
                    Gid = GetProp(ws, "gid"),
                    Name = GetProp(ws, "name")
                });
            }
        }

        return new
        {
            Gid = GetProp(result, "gid"),
            Name = GetProp(result, "name"),
            Email = GetProp(result, "email"),
            Workspaces = workspaces
        };
    }

    // ───────────────────────────── Tasks ─────────────────────────────

    [Display(Name = "create_task")]
    [Description("Create a new task in Asana. Must provide a workspace or a project to place the task in.")]
    [Parameters("""{"type":"object","properties":{"name":{"type":"string","description":"Task name/title"},"notes":{"type":"string","description":"Plain text description"},"htmlNotes":{"type":"string","description":"Rich text description in HTML"},"assignee":{"type":"string","description":"Assignee: user GID, email, or 'me'"},"dueOn":{"type":"string","description":"Due date (YYYY-MM-DD)"},"dueAt":{"type":"string","description":"Due datetime (ISO 8601)"},"startOn":{"type":"string","description":"Start date (YYYY-MM-DD)"},"completed":{"type":"boolean","description":"Whether the task is completed"},"projectId":{"type":"string","description":"Project GID to add the task to"},"sectionId":{"type":"string","description":"Section GID to place the task in"},"parent":{"type":"string","description":"Parent task GID (creates a subtask)"},"workspace":{"type":"string","description":"Workspace GID (required if no project specified)"},"followers":{"type":"string","description":"Comma-separated user GIDs or emails to add as followers"},"customFields":{"type":"string","description":"JSON object mapping custom field GIDs to values"}},"required":["name"]}""")]
    public async Task<object> CreateTask(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Missing required parameter: name");

        var body = new Dictionary<string, object> { ["name"] = request.Name };

        if (!string.IsNullOrWhiteSpace(request.Notes)) body["notes"] = request.Notes;
        if (!string.IsNullOrWhiteSpace(request.HtmlNotes)) body["html_notes"] = request.HtmlNotes;
        if (!string.IsNullOrWhiteSpace(request.Assignee)) body["assignee"] = request.Assignee;
        if (!string.IsNullOrWhiteSpace(request.DueOn)) body["due_on"] = request.DueOn;
        if (!string.IsNullOrWhiteSpace(request.DueAt)) body["due_at"] = request.DueAt;
        if (!string.IsNullOrWhiteSpace(request.StartOn)) body["start_on"] = request.StartOn;
        if (request.Completed.HasValue) body["completed"] = request.Completed.Value;
        if (!string.IsNullOrWhiteSpace(request.Parent)) body["parent"] = request.Parent;
        if (!string.IsNullOrWhiteSpace(request.Workspace)) body["workspace"] = request.Workspace;

        if (!string.IsNullOrWhiteSpace(request.ProjectId))
            body["projects"] = new[] { request.ProjectId };

        if (!string.IsNullOrWhiteSpace(request.SectionId))
            body["memberships"] = new[] { new { project = request.ProjectId, section = request.SectionId } };

        if (!string.IsNullOrWhiteSpace(request.Followers))
            body["followers"] = request.Followers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (!string.IsNullOrWhiteSpace(request.CustomFields))
            body["custom_fields"] = JsonSerializer.Deserialize<Dictionary<string, object>>(request.CustomFields);

        var result = await _client.PostAsync("/tasks", body);

        return new
        {
            Success = true,
            TaskGid = GetProp(result, "gid"),
            Message = $"Task '{request.Name}' created"
        };
    }

    [Display(Name = "get_task")]
    [Description("Get full details of an Asana task by its GID. Optionally include subtasks and comments.")]
    [Parameters("""{"type":"object","properties":{"taskId":{"type":"string","description":"The task GID"},"optFields":{"type":"string","description":"Comma-separated fields to include"},"includeSubtasks":{"type":"boolean","description":"Also fetch subtasks (default: false)"},"includeComments":{"type":"boolean","description":"Also fetch comments/stories (default: false)"}},"required":["taskId"]}""")]
    public async Task<object> GetTask(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId))
            throw new ArgumentException("Missing required parameter: taskId");

        var defaultFields = "name,notes,assignee,assignee.name,due_on,due_at,start_on,completed,completed_at,projects,projects.name,parent,parent.name,custom_fields,permalink_url,created_at,modified_at";
        var query = BuildQuery(("opt_fields", request.OptFields ?? defaultFields));
        var result = await _client.GetAsync($"/tasks/{request.TaskId}{query}");

        var task = new Dictionary<string, object>
        {
            ["Gid"] = GetProp(result, "gid"),
            ["Name"] = GetProp(result, "name"),
            ["Notes"] = GetProp(result, "notes"),
            ["Completed"] = result.TryGetProperty("completed", out var comp) && comp.ValueKind == JsonValueKind.True,
            ["CompletedAt"] = GetProp(result, "completed_at"),
            ["DueOn"] = GetProp(result, "due_on"),
            ["DueAt"] = GetProp(result, "due_at"),
            ["StartOn"] = GetProp(result, "start_on"),
            ["Assignee"] = GetNestedNameAndGid(result, "assignee"),
            ["Parent"] = GetNestedNameAndGid(result, "parent"),
            ["PermalinkUrl"] = GetProp(result, "permalink_url"),
            ["CreatedAt"] = GetProp(result, "created_at"),
            ["ModifiedAt"] = GetProp(result, "modified_at")
        };

        if (result.TryGetProperty("projects", out var projects))
        {
            var projectList = new List<object>();
            foreach (var p in projects.EnumerateArray())
                projectList.Add(new { Gid = GetProp(p, "gid"), Name = GetProp(p, "name") });
            task["Projects"] = projectList;
        }

        if (request.IncludeSubtasks == true)
        {
            var subtasks = await _client.GetAsync($"/tasks/{request.TaskId}/subtasks?opt_fields=name,completed,assignee,assignee.name,due_on");
            var subtaskList = new List<object>();
            foreach (var s in subtasks.EnumerateArray())
            {
                subtaskList.Add(new
                {
                    Gid = GetProp(s, "gid"),
                    Name = GetProp(s, "name"),
                    Completed = s.TryGetProperty("completed", out var sc) && sc.ValueKind == JsonValueKind.True,
                    DueOn = GetProp(s, "due_on"),
                    Assignee = GetNestedNameAndGid(s, "assignee")
                });
            }
            task["Subtasks"] = subtaskList;
        }

        if (request.IncludeComments == true)
        {
            var stories = await _client.GetAsync($"/tasks/{request.TaskId}/stories?opt_fields=text,created_by,created_by.name,created_at,type&limit=50");
            var storyList = new List<object>();
            foreach (var s in stories.EnumerateArray())
            {
                if (GetProp(s, "type") == "comment")
                {
                    storyList.Add(new
                    {
                        Gid = GetProp(s, "gid"),
                        Text = GetProp(s, "text"),
                        CreatedBy = GetNestedNameAndGid(s, "created_by"),
                        CreatedAt = GetProp(s, "created_at")
                    });
                }
            }
            task["Comments"] = storyList;
        }

        return task;
    }

    [Display(Name = "update_task")]
    [Description("Update properties of an existing Asana task.")]
    [Parameters("""{"type":"object","properties":{"taskId":{"type":"string","description":"The task GID"},"name":{"type":"string","description":"Updated task name"},"notes":{"type":"string","description":"Updated plain text description"},"htmlNotes":{"type":"string","description":"Updated rich text description in HTML"},"assignee":{"type":"string","description":"Updated assignee: user GID, email, or 'me'"},"dueOn":{"type":"string","description":"Updated due date (YYYY-MM-DD)"},"dueAt":{"type":"string","description":"Updated due datetime (ISO 8601)"},"startOn":{"type":"string","description":"Updated start date (YYYY-MM-DD)"},"completed":{"type":"boolean","description":"Mark task as completed or incomplete"}},"required":["taskId"]}""")]
    public async Task<object> UpdateTask(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId))
            throw new ArgumentException("Missing required parameter: taskId");

        var body = new Dictionary<string, object>();

        if (request.Name != null) body["name"] = request.Name;
        if (request.Notes != null) body["notes"] = request.Notes;
        if (request.HtmlNotes != null) body["html_notes"] = request.HtmlNotes;
        if (request.Assignee != null) body["assignee"] = request.Assignee;
        if (request.DueOn != null) body["due_on"] = request.DueOn;
        if (request.DueAt != null) body["due_at"] = request.DueAt;
        if (request.StartOn != null) body["start_on"] = request.StartOn;
        if (request.Completed.HasValue) body["completed"] = request.Completed.Value;

        if (body.Count == 0)
            return new { Success = false, Message = "No properties to update" };

        await _client.PutAsync($"/tasks/{request.TaskId}", body);

        return new { Success = true, Message = $"Task {request.TaskId} updated" };
    }

    [Display(Name = "delete_task")]
    [Description("Permanently delete an Asana task by its GID.")]
    [Parameters("""{"type":"object","properties":{"taskId":{"type":"string","description":"The task GID to delete"}},"required":["taskId"]}""")]
    public async Task<object> DeleteTask(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId))
            throw new ArgumentException("Missing required parameter: taskId");

        await _client.DeleteAsync($"/tasks/{request.TaskId}");

        return new { Success = true, Message = $"Task {request.TaskId} deleted" };
    }

    [Display(Name = "search_tasks")]
    [Description("Search for tasks in a workspace using text, assignee, project, date, and completion filters.")]
    [Parameters("""{"type":"object","properties":{"workspace":{"type":"string","description":"Workspace GID (required)"},"text":{"type":"string","description":"Full-text search query"},"assigneeAny":{"type":"string","description":"Comma-separated user GIDs to filter by assignee"},"projectsAny":{"type":"string","description":"Comma-separated project GIDs to filter by project"},"completed":{"type":"boolean","description":"Filter by completion status"},"dueOnBefore":{"type":"string","description":"Tasks due before this date (YYYY-MM-DD)"},"dueOnAfter":{"type":"string","description":"Tasks due after this date (YYYY-MM-DD)"},"sortBy":{"type":"string","description":"Sort field: due_date, created_at, completed_at, likes, modified_at (default: modified_at)"},"sortAscending":{"type":"boolean","description":"Sort ascending (default: false)"},"optFields":{"type":"string","description":"Comma-separated fields to include"},"limit":{"type":"integer","description":"Max results (1-100)"}},"required":["workspace"]}""")]
    public async Task<object> SearchTasks(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Workspace))
            throw new ArgumentException("Missing required parameter: workspace");

        var defaultFields = "name,completed,assignee,assignee.name,due_on,projects,projects.name,permalink_url";
        var query = BuildQuery(
            ("text", request.Text),
            ("assignee.any", request.AssigneeAny),
            ("projects.any", request.ProjectsAny),
            ("completed", request.Completed?.ToString().ToLowerInvariant()),
            ("due_on.before", request.DueOnBefore),
            ("due_on.after", request.DueOnAfter),
            ("sort_by", request.SortBy ?? "modified_at"),
            ("sort_ascending", request.SortAscending?.ToString().ToLowerInvariant()),
            ("opt_fields", request.OptFields ?? defaultFields),
            ("limit", (request.Limit ?? 25).ToString()));

        var result = await _client.GetAsync($"/workspaces/{request.Workspace}/tasks/search{query}");

        var tasks = new List<object>();
        foreach (var t in result.EnumerateArray())
        {
            tasks.Add(new
            {
                Gid = GetProp(t, "gid"),
                Name = GetProp(t, "name"),
                Completed = t.TryGetProperty("completed", out var comp) && comp.ValueKind == JsonValueKind.True,
                DueOn = GetProp(t, "due_on"),
                Assignee = GetNestedNameAndGid(t, "assignee"),
                PermalinkUrl = GetProp(t, "permalink_url")
            });
        }

        return new { Count = tasks.Count, Tasks = tasks };
    }

    [Display(Name = "get_tasks")]
    [Description("List tasks filtered by project, section, or assignee. Provide at least one filter.")]
    [Parameters("""{"type":"object","properties":{"projectId":{"type":"string","description":"Project GID to list tasks from"},"sectionId":{"type":"string","description":"Section GID to list tasks from"},"assignee":{"type":"string","description":"User GID or 'me' to filter by assignee (requires workspace)"},"workspace":{"type":"string","description":"Workspace GID (required when filtering by assignee)"},"completed":{"type":"boolean","description":"Filter by completion status"},"optFields":{"type":"string","description":"Comma-separated fields to include"},"limit":{"type":"integer","description":"Max results per page (1-100)"},"offset":{"type":"string","description":"Pagination offset token"}},"required":[]}""")]
    public async Task<object> GetTasks(ServiceConfig config, ServiceRequest request)
    {
        var defaultFields = "name,completed,assignee,assignee.name,due_on,projects,projects.name";
        var query = BuildQuery(
            ("project", request.ProjectId),
            ("section", request.SectionId),
            ("assignee", request.Assignee),
            ("workspace", request.Workspace),
            ("completed_since", request.Completed == false ? "now" : null),
            ("opt_fields", request.OptFields ?? defaultFields),
            ("limit", (request.Limit ?? 50).ToString()),
            ("offset", request.Offset));

        var raw = await _client.GetRawAsync($"/tasks{query}");
        var data = raw.TryGetProperty("data", out var d) ? d : raw;

        var tasks = new List<object>();
        foreach (var t in data.EnumerateArray())
        {
            tasks.Add(new
            {
                Gid = GetProp(t, "gid"),
                Name = GetProp(t, "name"),
                Completed = t.TryGetProperty("completed", out var comp) && comp.ValueKind == JsonValueKind.True,
                DueOn = GetProp(t, "due_on"),
                Assignee = GetNestedNameAndGid(t, "assignee")
            });
        }

        string nextOffset = null;
        if (raw.TryGetProperty("next_page", out var np) && np.ValueKind == JsonValueKind.Object)
            nextOffset = GetProp(np, "offset");

        return new { Count = tasks.Count, Tasks = tasks, NextOffset = nextOffset };
    }

    [Display(Name = "set_parent_for_task")]
    [Description("Set a parent task for an Asana task, making it a subtask.")]
    [Parameters("""{"type":"object","properties":{"taskId":{"type":"string","description":"The task GID to reparent"},"parent":{"type":"string","description":"The parent task GID (or null to remove parent)"}},"required":["taskId","parent"]}""")]
    public async Task<object> SetParentForTask(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId))
            throw new ArgumentException("Missing required parameter: taskId");
        if (string.IsNullOrWhiteSpace(request.Parent))
            throw new ArgumentException("Missing required parameter: parent");

        await _client.PostAsync($"/tasks/{request.TaskId}/setParent", new { parent = request.Parent });

        return new { Success = true, Message = $"Task {request.TaskId} set as subtask of {request.Parent}" };
    }

    [Display(Name = "add_task_followers")]
    [Description("Add followers to an Asana task so they receive notifications about it.")]
    [Parameters("""{"type":"object","properties":{"taskId":{"type":"string","description":"The task GID"},"followers":{"type":"string","description":"Comma-separated user GIDs or emails to add as followers"}},"required":["taskId","followers"]}""")]
    public async Task<object> AddTaskFollowers(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId))
            throw new ArgumentException("Missing required parameter: taskId");
        if (string.IsNullOrWhiteSpace(request.Followers))
            throw new ArgumentException("Missing required parameter: followers");

        var followerList = request.Followers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        await _client.PostAsync($"/tasks/{request.TaskId}/addFollowers", new { followers = followerList });

        return new { Success = true, Message = $"Added {followerList.Length} follower(s) to task {request.TaskId}" };
    }

    [Display(Name = "move_task_to_section")]
    [Description("Move an existing task to a different section (column) within its project. Use this to move tasks between board columns like Today, Past-Due, Completed, etc.")]
    [Parameters("""{"type":"object","properties":{"taskId":{"type":"string","description":"The task GID to move"},"sectionId":{"type":"string","description":"The target section GID"}},"required":["taskId","sectionId"]}""")]
    public async Task<object> MoveTaskToSection(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId))
            throw new ArgumentException("Missing required parameter: taskId");
        if (string.IsNullOrWhiteSpace(request.SectionId))
            throw new ArgumentException("Missing required parameter: sectionId");

        await _client.PostAsync($"/sections/{request.SectionId}/addTask", new { task = request.TaskId });

        return new { Success = true, Message = $"Task {request.TaskId} moved to section {request.SectionId}" };
    }

    // ───────────────────────────── Projects ─────────────────────────────

    [Display(Name = "get_projects")]
    [Description("List projects in a workspace, optionally filtered by team or archived status.")]
    [Parameters("""{"type":"object","properties":{"workspace":{"type":"string","description":"Workspace GID (required)"},"team":{"type":"string","description":"Team GID to filter by"},"archived":{"type":"boolean","description":"Filter by archived status (default: false)"},"optFields":{"type":"string","description":"Comma-separated fields to include"},"limit":{"type":"integer","description":"Max results per page (1-100)"},"offset":{"type":"string","description":"Pagination offset token"}},"required":["workspace"]}""")]
    public async Task<object> GetProjects(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Workspace))
            throw new ArgumentException("Missing required parameter: workspace");

        var query = BuildQuery(
            ("workspace", request.Workspace),
            ("team", request.Team),
            ("archived", (request.Archived ?? false).ToString().ToLowerInvariant()),
            ("opt_fields", request.OptFields ?? "name,owner,owner.name,color,created_at,modified_at"),
            ("limit", (request.Limit ?? 50).ToString()),
            ("offset", request.Offset));

        var raw = await _client.GetRawAsync($"/projects{query}");
        var data = raw.TryGetProperty("data", out var d) ? d : raw;

        var projects = new List<object>();
        foreach (var p in data.EnumerateArray())
        {
            projects.Add(new
            {
                Gid = GetProp(p, "gid"),
                Name = GetProp(p, "name"),
                Owner = GetNestedNameAndGid(p, "owner"),
                Color = GetProp(p, "color")
            });
        }

        string nextOffset = null;
        if (raw.TryGetProperty("next_page", out var np) && np.ValueKind == JsonValueKind.Object)
            nextOffset = GetProp(np, "offset");

        return new { Count = projects.Count, Projects = projects, NextOffset = nextOffset };
    }

    [Display(Name = "get_project")]
    [Description("Get full details of an Asana project by its GID.")]
    [Parameters("""{"type":"object","properties":{"projectId":{"type":"string","description":"The project GID"},"optFields":{"type":"string","description":"Comma-separated fields to include"}},"required":["projectId"]}""")]
    public async Task<object> GetProject(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            throw new ArgumentException("Missing required parameter: projectId");

        var defaultFields = "name,notes,owner,owner.name,team,team.name,color,created_at,modified_at,due_on,start_on,permalink_url,archived,members,members.name";
        var query = BuildQuery(("opt_fields", request.OptFields ?? defaultFields));
        var result = await _client.GetAsync($"/projects/{request.ProjectId}{query}");

        var members = new List<object>();
        if (result.TryGetProperty("members", out var membersList))
        {
            foreach (var m in membersList.EnumerateArray())
                members.Add(new { Gid = GetProp(m, "gid"), Name = GetProp(m, "name") });
        }

        return new
        {
            Gid = GetProp(result, "gid"),
            Name = GetProp(result, "name"),
            Notes = GetProp(result, "notes"),
            Owner = GetNestedNameAndGid(result, "owner"),
            Team = GetNestedNameAndGid(result, "team"),
            Color = GetProp(result, "color"),
            DueOn = GetProp(result, "due_on"),
            StartOn = GetProp(result, "start_on"),
            Archived = result.TryGetProperty("archived", out var arch) && arch.ValueKind == JsonValueKind.True,
            PermalinkUrl = GetProp(result, "permalink_url"),
            Members = members
        };
    }

    [Display(Name = "get_project_sections")]
    [Description("List all sections (columns) in an Asana project.")]
    [Parameters("""{"type":"object","properties":{"projectId":{"type":"string","description":"The project GID"},"optFields":{"type":"string","description":"Comma-separated fields to include"},"limit":{"type":"integer","description":"Max results per page (1-100)"}},"required":["projectId"]}""")]
    public async Task<object> GetProjectSections(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            throw new ArgumentException("Missing required parameter: projectId");

        var query = BuildQuery(
            ("opt_fields", request.OptFields ?? "name,created_at"),
            ("limit", (request.Limit ?? 100).ToString()));

        var result = await _client.GetAsync($"/projects/{request.ProjectId}/sections{query}");

        var sections = new List<object>();
        foreach (var s in result.EnumerateArray())
        {
            sections.Add(new
            {
                Gid = GetProp(s, "gid"),
                Name = GetProp(s, "name")
            });
        }

        return new { Sections = sections };
    }

    // ───────────────────────────── Stories / Comments ─────────────────────────────

    [Display(Name = "create_task_story")]
    [Description("Add a comment or story to an Asana task.")]
    [Parameters("""{"type":"object","properties":{"taskId":{"type":"string","description":"The task GID to comment on"},"storyText":{"type":"string","description":"Plain text comment"},"htmlText":{"type":"string","description":"Rich text comment in HTML"}},"required":["taskId","storyText"]}""")]
    public async Task<object> CreateTaskStory(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId))
            throw new ArgumentException("Missing required parameter: taskId");
        if (string.IsNullOrWhiteSpace(request.StoryText))
            throw new ArgumentException("Missing required parameter: storyText");

        var body = new Dictionary<string, object> { ["text"] = request.StoryText };
        if (!string.IsNullOrWhiteSpace(request.HtmlText))
            body["html_text"] = request.HtmlText;

        var result = await _client.PostAsync($"/tasks/{request.TaskId}/stories", body);

        return new
        {
            Success = true,
            StoryGid = GetProp(result, "gid"),
            Message = $"Comment added to task {request.TaskId}"
        };
    }

    [Display(Name = "get_stories_for_task")]
    [Description("Get the activity feed (comments, status changes) for an Asana task.")]
    [Parameters("""{"type":"object","properties":{"taskId":{"type":"string","description":"The task GID"},"optFields":{"type":"string","description":"Comma-separated fields to include"},"limit":{"type":"integer","description":"Max results per page (1-100)"}},"required":["taskId"]}""")]
    public async Task<object> GetStoriesForTask(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId))
            throw new ArgumentException("Missing required parameter: taskId");

        var query = BuildQuery(
            ("opt_fields", request.OptFields ?? "text,html_text,type,created_by,created_by.name,created_at"),
            ("limit", (request.Limit ?? 50).ToString()));

        var result = await _client.GetAsync($"/tasks/{request.TaskId}/stories{query}");

        var stories = new List<object>();
        foreach (var s in result.EnumerateArray())
        {
            stories.Add(new
            {
                Gid = GetProp(s, "gid"),
                Type = GetProp(s, "type"),
                Text = GetProp(s, "text"),
                CreatedBy = GetNestedNameAndGid(s, "created_by"),
                CreatedAt = GetProp(s, "created_at")
            });
        }

        return new { Stories = stories };
    }

    // ───────────────────────────── Search ─────────────────────────────

    [Display(Name = "typeahead_search")]
    [Description("Typeahead search across Asana resources (tasks, projects, users, etc.) in a workspace.")]
    [Parameters("""{"type":"object","properties":{"workspace":{"type":"string","description":"Workspace GID (required)"},"query":{"type":"string","description":"Search query string"},"resourceType":{"type":"string","description":"Type to search: task, project, user, tag, portfolio (default: task)"},"count":{"type":"integer","description":"Max results (1-100, default: 20)"},"optFields":{"type":"string","description":"Comma-separated fields to include"}},"required":["workspace","query"]}""")]
    public async Task<object> TypeaheadSearch(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Workspace))
            throw new ArgumentException("Missing required parameter: workspace");
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Missing required parameter: query");

        var resourceType = string.IsNullOrWhiteSpace(request.ResourceType) ? "task" : request.ResourceType;
        var query = BuildQuery(
            ("query", request.Query),
            ("resource_type", resourceType),
            ("count", (request.Count ?? 20).ToString()),
            ("opt_fields", request.OptFields ?? "name,completed"));

        var result = await _client.GetAsync($"/workspaces/{request.Workspace}/typeahead{query}");

        var items = new List<object>();
        foreach (var item in result.EnumerateArray())
        {
            items.Add(new
            {
                Gid = GetProp(item, "gid"),
                Name = GetProp(item, "name"),
                ResourceType = GetProp(item, "resource_type")
            });
        }

        return new { Count = items.Count, Results = items };
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private static string GetProp(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static object GetNestedNameAndGid(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return null;
        return new { Gid = GetProp(nested, "gid"), Name = GetProp(nested, "name") };
    }

    private static string BuildQuery(params (string key, string value)[] parameters)
    {
        var pairs = parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.value))
            .Select(p => $"{Uri.EscapeDataString(p.key)}={Uri.EscapeDataString(p.value)}");

        var query = string.Join("&", pairs);
        return string.IsNullOrEmpty(query) ? "" : $"?{query}";
    }
}
