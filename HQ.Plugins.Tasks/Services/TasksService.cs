using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HQ.Plugins.Tasks.Entities;
using Microsoft.EntityFrameworkCore;

namespace HQ.Plugins.Tasks.Services;

/// <summary>
/// Shared CRUD logic used by both the agent-tool and HTTP-endpoint code paths.
/// Callers pass the organisation id so data stays tenant-isolated.
/// </summary>
public class TasksService
{
    private readonly TasksDbContext _db;

    public TasksService(TasksDbContext db)
    {
        _db = db;
    }

    // Projects ---------------------------------------------------------------

    public Task<List<Project>> ListProjectsAsync(Guid orgId, bool includeArchived = false)
        => _db.Projects
            .Where(p => p.OrganizationId == orgId && (includeArchived || p.ArchivedAt == null))
            .OrderBy(p => p.Name)
            .ToListAsync();

    public async Task<Project> CreateProjectAsync(Guid orgId, string name, string description, string color)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = name,
            Description = description,
            Color = color,
            CreatedAt = DateTime.UtcNow
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    public async Task<Project> UpdateProjectAsync(Guid orgId, Guid id, string name, string description, string color, bool? archive)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OrganizationId == orgId);
        if (project == null) return null;
        if (name != null) project.Name = name;
        if (description != null) project.Description = description;
        if (color != null) project.Color = color;
        if (archive.HasValue) project.ArchivedAt = archive.Value ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync();
        return project;
    }

    public async Task<bool> DeleteProjectAsync(Guid orgId, Guid id)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OrganizationId == orgId);
        if (project == null) return false;
        // Cascade-delete tasks + comments for this project
        var taskIds = await _db.Tasks.Where(t => t.ProjectId == id).Select(t => t.Id).ToListAsync();
        if (taskIds.Count > 0)
        {
            _db.Comments.RemoveRange(_db.Comments.Where(c => taskIds.Contains(c.TaskId)));
            _db.Tasks.RemoveRange(_db.Tasks.Where(t => t.ProjectId == id));
        }
        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();
        return true;
    }

    // Tasks ------------------------------------------------------------------

    public Task<List<TaskItem>> ListTasksAsync(Guid orgId, Guid? projectId, string status, string assignee)
    {
        var q = _db.Tasks.Where(t => t.OrganizationId == orgId);
        if (projectId.HasValue) q = q.Where(t => t.ProjectId == projectId.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(assignee)) q = q.Where(t => t.Assignee == assignee);
        return q.OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ToListAsync();
    }

    public async Task<TaskItem> CreateTaskAsync(Guid orgId, Guid projectId, string title, string description,
        string assignee, DateTime? due)
    {
        var maxSort = await _db.Tasks
            .Where(t => t.ProjectId == projectId)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync() ?? 0;

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            ProjectId = projectId,
            Title = title,
            Description = description,
            Status = "todo",
            Assignee = assignee,
            Due = due,
            CreatedAt = DateTime.UtcNow,
            SortOrder = maxSort + 1
        };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();
        return task;
    }

    public async Task<TaskItem> UpdateTaskAsync(Guid orgId, Guid id, string title, string description,
        string status, string assignee, DateTime? due, int? sortOrder)
    {
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.OrganizationId == orgId);
        if (task == null) return null;
        if (title != null) task.Title = title;
        if (description != null) task.Description = description;
        if (status != null)
        {
            task.Status = status;
            task.CompletedAt = status == "done" ? DateTime.UtcNow : null;
        }
        if (assignee != null) task.Assignee = assignee;
        if (due.HasValue) task.Due = due;
        if (sortOrder.HasValue) task.SortOrder = sortOrder.Value;
        await _db.SaveChangesAsync();
        return task;
    }

    public async Task<bool> DeleteTaskAsync(Guid orgId, Guid id)
    {
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.OrganizationId == orgId);
        if (task == null) return false;
        _db.Comments.RemoveRange(_db.Comments.Where(c => c.TaskId == id));
        _db.Tasks.Remove(task);
        await _db.SaveChangesAsync();
        return true;
    }

    // Comments ---------------------------------------------------------------

    public async Task<Comment> AddCommentAsync(Guid orgId, Guid taskId, string author, string text)
    {
        var taskBelongs = await _db.Tasks.AnyAsync(t => t.Id == taskId && t.OrganizationId == orgId);
        if (!taskBelongs) return null;
        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            Author = author,
            Text = text,
            CreatedAt = DateTime.UtcNow
        };
        _db.Comments.Add(comment);
        await _db.SaveChangesAsync();
        return comment;
    }

    public async Task<List<Comment>> ListCommentsAsync(Guid orgId, Guid taskId)
    {
        var taskBelongs = await _db.Tasks.AnyAsync(t => t.Id == taskId && t.OrganizationId == orgId);
        if (!taskBelongs) return new List<Comment>();
        return await _db.Comments.Where(c => c.TaskId == taskId).OrderBy(c => c.CreatedAt).ToListAsync();
    }
}
