using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace HQ.Plugins.Tasks.Entities;

[Table("tasks", Schema = "plugin_localtasks")]
public class TaskItem
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    // Scope: exactly one of these is set.
    //   ProjectId set → the task belongs to a (shared) project.
    //   AgentId set    → the task is private to that agent (no project).
    public Guid? ProjectId { get; set; }
    public Guid? AgentId { get; set; }
    public string AgentName { get; set; }

    public string Title { get; set; }
    public string Description { get; set; }
    public string Status { get; set; } = "todo";    // todo | doing | done | blocked
    public string Assignee { get; set; }
    public DateTime? Due { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int SortOrder { get; set; }
}
