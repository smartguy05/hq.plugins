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
}

public class TasksToolImpl
{
    [Display(Name = "list_projects")]
    [Description("List all projects for the caller's organisation.")]
    [Parameters(typeof(ListProjectsArgs))]
    public Task<object> ListProjects(ServiceConfig config, ListProjectsArgs request)
        => WithService(svc => svc.ListProjectsAsync(RequireOrg(request.OrganizationId)));

    [Display(Name = "create_project")]
    [Description("Create a new project.")]
    [Parameters(typeof(CreateProjectArgs))]
    public Task<object> CreateProject(ServiceConfig config, CreateProjectArgs request)
        => WithService(svc => svc.CreateProjectAsync(RequireOrg(request.OrganizationId), request.Name, request.Description, request.Color));

    [Display(Name = "update_project")]
    [Description("Update an existing project's name, description, color, or archive state.")]
    [Parameters(typeof(UpdateProjectArgs))]
    public Task<object> UpdateProject(ServiceConfig config, UpdateProjectArgs request)
        => WithService(svc => svc.UpdateProjectAsync(RequireOrg(request.OrganizationId), request.ProjectId ?? Guid.Empty,
            request.Name, request.Description, request.Color, archive: null));

    [Display(Name = "delete_project")]
    [Description("Delete a project along with all of its tasks and comments.")]
    [Parameters(typeof(DeleteProjectArgs))]
    public Task<object> DeleteProject(ServiceConfig config, DeleteProjectArgs request)
        => WithService(svc => svc.DeleteProjectAsync(RequireOrg(request.OrganizationId), request.ProjectId ?? Guid.Empty));

    [Display(Name = "list_tasks")]
    [Description("List tasks. With a projectId, returns that project's tasks. Without one, returns the calling agent's own (project-less) tasks. Optionally filter by status or assignee.")]
    [Parameters(typeof(ListTasksArgs))]
    public Task<object> ListTasks(ServiceConfig config, ListTasksArgs request)
        => WithService(svc => svc.ListTasksAsync(RequireOrg(request.OrganizationId), request.ProjectId,
            request.ProjectId.HasValue ? null : RequireAgent(config), request.Status, request.Assignee));

    [Display(Name = "create_task")]
    [Description("Create a task. Supply a projectId to file it under a (shared) project; omit it and the task is private to the calling agent.")]
    [Parameters(typeof(CreateTaskArgs))]
    public Task<object> CreateTask(ServiceConfig config, CreateTaskArgs request)
        => WithService(svc => svc.CreateTaskAsync(RequireOrg(request.OrganizationId), request.ProjectId,
            request.ProjectId.HasValue ? null : RequireAgent(config), config.AgentName,
            request.Title, request.Description, request.Assignee, request.Due));

    [Display(Name = "update_task")]
    [Description("Update a task's title, description, status, assignee, or due date.")]
    [Parameters(typeof(UpdateTaskArgs))]
    public Task<object> UpdateTask(ServiceConfig config, UpdateTaskArgs request)
        => WithService(svc => svc.UpdateTaskAsync(RequireOrg(request.OrganizationId), request.TaskId ?? Guid.Empty, request.Title,
            request.Description, request.Status, request.Assignee, request.Due, request.SortOrder, config.AgentId));

    [Display(Name = "complete_task")]
    [Description("Mark a task as done (shorthand for update_task with status='done').")]
    [Parameters(typeof(CompleteTaskArgs))]
    public Task<object> CompleteTask(ServiceConfig config, CompleteTaskArgs request)
        => WithService(svc => svc.UpdateTaskAsync(RequireOrg(request.OrganizationId), request.TaskId ?? Guid.Empty,
            null, null, "done", null, null, null, config.AgentId));

    [Display(Name = "delete_task")]
    [Description("Delete a task and its comments.")]
    [Parameters(typeof(DeleteTaskArgs))]
    public Task<object> DeleteTask(ServiceConfig config, DeleteTaskArgs request)
        => WithService(svc => svc.DeleteTaskAsync(RequireOrg(request.OrganizationId), request.TaskId ?? Guid.Empty, config.AgentId));

    [Display(Name = "add_comment")]
    [Description("Add a comment to a task.")]
    [Parameters(typeof(AddCommentArgs))]
    public Task<object> AddComment(ServiceConfig config, AddCommentArgs request)
        => WithService(svc => svc.AddCommentAsync(RequireOrg(request.OrganizationId), request.TaskId ?? Guid.Empty,
            request.Author ?? "agent", request.Text, config.AgentId));

    [Display(Name = "list_comments")]
    [Description("List comments on a task.")]
    [Parameters(typeof(ListCommentsArgs))]
    public Task<object> ListComments(ServiceConfig config, ListCommentsArgs request)
        => WithService(svc => svc.ListCommentsAsync(RequireOrg(request.OrganizationId), request.TaskId ?? Guid.Empty, config.AgentId));

    private static Guid RequireOrg(Guid? organizationId)
    {
        if (!organizationId.HasValue || organizationId.Value == Guid.Empty)
            throw new InvalidOperationException("organizationId is required.");
        return organizationId.Value;
    }

    // The agent owning this plugin config — used to scope project-less tasks. Populated by the host.
    private static Guid RequireAgent(ServiceConfig config)
    {
        if (!config.AgentId.HasValue || config.AgentId.Value == Guid.Empty)
            throw new InvalidOperationException("No agent context available; supply a projectId for this task.");
        return config.AgentId.Value;
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
