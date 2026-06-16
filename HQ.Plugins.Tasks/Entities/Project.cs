using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace HQ.Plugins.Tasks.Entities;

[Table("projects", Schema = "plugin_localtasks")]
public class Project
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Color { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
}
