using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Tools;
using HQ.Plugins.Tasks.Models;
using HQ.Plugins.Tasks.Services;

namespace HQ.Plugins.Tasks.Tools;

/// <summary>
/// Agent-facing tools. Methods are the reflection source for tool definitions.
/// All methods require an OrganizationId on the request — the agent caller must
/// supply it (typically injected by the orchestrator from the agent's org).
/// </summary>
public static class TasksTools
{
    public static List<ToolCall> GetToolDefinitions()
        => ServiceExtensions.GetServiceToolCalls<TasksToolImpl>();

    public static async Task<object> InvokeAsync(ServiceRequest request, ServiceConfig config)
    {
        var impl = new TasksToolImpl();
        return await impl.Dispatch(request, config);
    }
}

public class TasksToolImpl
{
    [Display(Name = "list_projects")]
    [Description("List all projects for the caller's organisation.")]
    [Parameters("""{"type":"object","properties":{"organizationId":{"type":"string","description":"Organisation GUID"}},"required":["organizationId"]}""")]
    public Task<object> ListProjects(ServiceConfig config, ServiceRequest request)
        => WithService(svc => svc.ListProjectsAsync(RequireOrg(request)));

    [Display(Name = "create_project")]
    [Description("Create a new project.")]
    [Parameters("""{"type":"object","properties":{"organizationId":{"type":"string"},"name":{"type":"string"},"description":{"type":"string"},"color":{"type":"string"}},"required":["organizationId","name"]}""")]
    public Task<object> CreateProject(ServiceConfig config, ServiceRequest request)
        => WithService(svc => svc.CreateProjectAsync(RequireOrg(request), request.Name, request.Description, request.Color));

    [Display(Name = "update_project")]
    [Description("Update an existing project's name, description, color, or archive state.")]
    [Parameters("""{"type":"object","properties":{"organizationId":{"type":"string"},"projectId":{"type":"string"},"name":{"type":"string"},"description":{"type":"string"},"color":{"type":"string"}},"required":["organizationId","projectId"]}""")]
    public Task<object> UpdateProject(ServiceConfig config, ServiceRequest request)
        => WithService(svc => svc.UpdateProjectAsync(RequireOrg(request), request.ProjectId ?? Guid.Empty,
            request.Name, request.Description, request.Color, archive: null));

    [Display(Name = "delete_project")]
    [Description("Delete a project along with all of its tasks and comments.")]
    [Parameters("""{"type":"object","properties":{"organizationId":{"type":"string"},"projectId":{"type":"string"}},"required":["organizationId","projectId"]}""")]
    public Task<object> DeleteProject(ServiceConfig config, ServiceRequest request)
        => WithService(svc => svc.DeleteProjectAsync(RequireOrg(request), request.ProjectId ?? Guid.Empty));

    [Display(Name = "list_tasks")]
    [Description("List tasks, optionally filtered by project, status, or assignee.")]
    [Parameters("""{"type":"object","properties":{"organizationId":{"type":"string"},"projectId":{"type":"string"},"status":{"type":"string","enum":["todo","doing","done","blocked"]},"assignee":{"type":"string"}},"required":["organizationId"]}""")]
    public Task<object> ListTasks(ServiceConfig config, ServiceRequest request)
        => WithService(svc => svc.ListTasksAsync(RequireOrg(request), request.ProjectId, request.Status, request.Assignee));

    [Display(Name = "create_task")]
    [Description("Create a new task inside a project.")]
    [Parameters("""{"type":"object","properties":{"organizationId":{"type":"string"},"projectId":{"type":"string"},"title":{"type":"string"},"description":{"type":"string"},"assignee":{"type":"string"},"due":{"type":"string","format":"date-time"}},"required":["organizationId","projectId","title"]}""")]
    public Task<object> CreateTask(ServiceConfig config, ServiceRequest request)
        => WithService(svc => svc.CreateTaskAsync(RequireOrg(request), request.ProjectId ?? Guid.Empty, request.Title,
            request.Description, request.Assignee, request.Due));

    [Display(Name = "update_task")]
    [Description("Update a task's title, description, status, assignee, or due date.")]
    [Parameters("""{"type":"object","properties":{"organizationId":{"type":"string"},"taskId":{"type":"string"},"title":{"type":"string"},"description":{"type":"string"},"status":{"type":"string","enum":["todo","doing","done","blocked"]},"assignee":{"type":"string"},"due":{"type":"string","format":"date-time"}},"required":["organizationId","taskId"]}""")]
    public Task<object> UpdateTask(ServiceConfig config, ServiceRequest request)
        => WithService(svc => svc.UpdateTaskAsync(RequireOrg(request), request.TaskId ?? Guid.Empty, request.Title,
            request.Description, request.Status, request.Assignee, request.Due, request.SortOrder));

    [Display(Name = "complete_task")]
    [Description("Mark a task as done (shorthand for update_task with status='done').")]
    [Parameters("""{"type":"object","properties":{"organizationId":{"type":"string"},"taskId":{"type":"string"}},"required":["organizationId","taskId"]}""")]
    public Task<object> CompleteTask(ServiceConfig config, ServiceRequest request)
        => WithService(svc => svc.UpdateTaskAsync(RequireOrg(request), request.TaskId ?? Guid.Empty,
            null, null, "done", null, null, null));

    [Display(Name = "delete_task")]
    [Description("Delete a task and its comments.")]
    [Parameters("""{"type":"object","properties":{"organizationId":{"type":"string"},"taskId":{"type":"string"}},"required":["organizationId","taskId"]}""")]
    public Task<object> DeleteTask(ServiceConfig config, ServiceRequest request)
        => WithService(svc => svc.DeleteTaskAsync(RequireOrg(request), request.TaskId ?? Guid.Empty));

    [Display(Name = "add_comment")]
    [Description("Add a comment to a task.")]
    [Parameters("""{"type":"object","properties":{"organizationId":{"type":"string"},"taskId":{"type":"string"},"text":{"type":"string"},"author":{"type":"string"}},"required":["organizationId","taskId","text"]}""")]
    public Task<object> AddComment(ServiceConfig config, ServiceRequest request)
        => WithService(svc => svc.AddCommentAsync(RequireOrg(request), request.TaskId ?? Guid.Empty,
            request.Author ?? "agent", request.Text));

    [Display(Name = "list_comments")]
    [Description("List comments on a task.")]
    [Parameters("""{"type":"object","properties":{"organizationId":{"type":"string"},"taskId":{"type":"string"}},"required":["organizationId","taskId"]}""")]
    public Task<object> ListComments(ServiceConfig config, ServiceRequest request)
        => WithService(svc => svc.ListCommentsAsync(RequireOrg(request), request.TaskId ?? Guid.Empty));

    // Dispatcher used by TasksCommand.DoWork — matches ServiceRequest.Method
    // (snake_case tool name) to the corresponding method above.
    public async Task<object> Dispatch(ServiceRequest request, ServiceConfig config)
    {
        return request.Method switch
        {
            "list_projects" => await ListProjects(config, request),
            "create_project" => await CreateProject(config, request),
            "update_project" => await UpdateProject(config, request),
            "delete_project" => await DeleteProject(config, request),
            "list_tasks" => await ListTasks(config, request),
            "create_task" => await CreateTask(config, request),
            "update_task" => await UpdateTask(config, request),
            "complete_task" => await CompleteTask(config, request),
            "delete_task" => await DeleteTask(config, request),
            "add_comment" => await AddComment(config, request),
            "list_comments" => await ListComments(config, request),
            _ => new { Success = false, Message = $"Unknown method '{request.Method}'." }
        };
    }

    private static Guid RequireOrg(ServiceRequest request)
    {
        if (!request.OrganizationId.HasValue || request.OrganizationId.Value == Guid.Empty)
            throw new InvalidOperationException("organizationId is required.");
        return request.OrganizationId.Value;
    }

    // Runs an operation against a short-lived DbContext-backed service, mirroring
    // how the HTTP endpoints build a context per call.
    private static async Task<object> WithService<T>(Func<TasksService, Task<T>> op)
    {
        using var ctx = TasksCommand.BuildDbContext();
        var svc = new TasksService(ctx);
        return (object)(await op(svc)) ?? new { };
    }
}
