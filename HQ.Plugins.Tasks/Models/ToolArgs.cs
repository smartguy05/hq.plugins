using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;

namespace HQ.Plugins.Tasks.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable).
/// </summary>

/// <summary>Args for tools that take no LLM parameters.</summary>
public class EmptyArgs;

public class ListProjectsArgs
{
    [Required, Description("Organisation GUID")]
    public Guid? OrganizationId { get; set; }
}

public class CreateProjectArgs
{
    [Required]
    public Guid? OrganizationId { get; set; }

    [Required]
    public string Name { get; set; }

    public string Description { get; set; }

    public string Color { get; set; }
}

public class UpdateProjectArgs
{
    [Required]
    public Guid? OrganizationId { get; set; }

    [Required]
    public Guid? ProjectId { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public string Color { get; set; }
}

public class DeleteProjectArgs
{
    [Required]
    public Guid? OrganizationId { get; set; }

    [Required]
    public Guid? ProjectId { get; set; }
}

public class ListTasksArgs
{
    [Required]
    public Guid? OrganizationId { get; set; }

    public Guid? ProjectId { get; set; }

    [SchemaEnum("todo", "doing", "done", "blocked")]
    public string Status { get; set; }

    public string Assignee { get; set; }
}

public class CreateTaskArgs
{
    [Required]
    public Guid? OrganizationId { get; set; }

    public Guid? ProjectId { get; set; }

    [Required]
    public string Title { get; set; }

    public string Description { get; set; }

    public string Assignee { get; set; }

    public DateTime? Due { get; set; }
}

public class UpdateTaskArgs
{
    [Required]
    public Guid? OrganizationId { get; set; }

    [Required]
    public Guid? TaskId { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    [SchemaEnum("todo", "doing", "done", "blocked")]
    public string Status { get; set; }

    public string Assignee { get; set; }

    public DateTime? Due { get; set; }

    /// <summary>Not advertised to the model; only used by the HTTP/UI reorder path.</summary>
    [Injected]
    public int? SortOrder { get; set; }
}

public class CompleteTaskArgs
{
    [Required]
    public Guid? OrganizationId { get; set; }

    [Required]
    public Guid? TaskId { get; set; }
}

public class DeleteTaskArgs
{
    [Required]
    public Guid? OrganizationId { get; set; }

    [Required]
    public Guid? TaskId { get; set; }
}

public class AddCommentArgs
{
    [Required]
    public Guid? OrganizationId { get; set; }

    [Required]
    public Guid? TaskId { get; set; }

    [Required]
    public string Text { get; set; }

    public string Author { get; set; }
}

public class ListCommentsArgs
{
    [Required]
    public Guid? OrganizationId { get; set; }

    [Required]
    public Guid? TaskId { get; set; }
}
