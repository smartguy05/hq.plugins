using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.Notion.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Each Notion tool method takes its own args type as the second parameter.
/// </summary>

public class SearchArgs
{
    [Description("Text to search titles for (empty returns all shared objects)")]
    public string Query { get; set; }

    [Description("Limit to 'page' or 'database'")]
    public string FilterType { get; set; }

    [Description("Max results (default 25)")]
    public int? PageSize { get; set; }
}

public class GetPageArgs
{
    [Required]
    public string PageId { get; set; }
}

public class CreatePageArgs
{
    [Required]
    public string ParentId { get; set; }

    [Description("'page' or 'database'")]
    public string ParentType { get; set; }

    [Description("Title (page parent, or a 'title' property)")]
    public string Title { get; set; }

    [Description("Optional body text")]
    public string Text { get; set; }

    [Description("Raw Notion properties object as JSON (overrides title)")]
    public string PropertiesJson { get; set; }

    [Description("Raw Notion block children array as JSON (overrides text)")]
    public string ChildrenJson { get; set; }
}

public class AppendBlockArgs
{
    [Required, Description("Page id or block id to append to")]
    public string BlockId { get; set; }

    public string Text { get; set; }

    [Description("Raw Notion block children array as JSON (overrides text)")]
    public string ChildrenJson { get; set; }
}

public class QueryDatabaseArgs
{
    [Required]
    public string DatabaseId { get; set; }

    [Description("Raw Notion filter object as JSON")]
    public string FilterJson { get; set; }

    [Description("Raw Notion sorts array as JSON")]
    public string SortsJson { get; set; }

    [Description("Max results (default 25)")]
    public int? PageSize { get; set; }
}

public class UpdatePageArgs
{
    [Required]
    public string PageId { get; set; }

    [Required, Description("Raw Notion properties object as JSON")]
    public string PropertiesJson { get; set; }
}
