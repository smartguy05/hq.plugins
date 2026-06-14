using System;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Tasks.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    public Guid? OrganizationId { get; set; }

    // Project
    public Guid? ProjectId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Color { get; set; }

    // Task
    public Guid? TaskId { get; set; }
    public string Title { get; set; }
    public string Status { get; set; }
    public string Assignee { get; set; }
    public DateTime? Due { get; set; }
    public int? SortOrder { get; set; }

    // Comment
    public string Text { get; set; }
    public string Author { get; set; }
}
