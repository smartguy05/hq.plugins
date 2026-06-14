using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace HQ.Plugins.Tasks.Entities;

[Table("tasks", Schema = "plugin_localtasks")]
public class TaskItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string Status { get; set; } = "todo";    // todo | doing | done | blocked
    public string Assignee { get; set; }
    public DateTime? Due { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int SortOrder { get; set; }
}
