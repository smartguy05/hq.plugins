using HQ.Models.Interfaces;

namespace HQ.Plugins.Notion.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Search
    public string Query { get; set; }
    public string FilterType { get; set; }   // "page" | "database"
    public int? PageSize { get; set; }

    // Object ids
    public string PageId { get; set; }
    public string BlockId { get; set; }
    public string DatabaseId { get; set; }

    // Create page
    public string ParentId { get; set; }
    public string ParentType { get; set; }    // "page" | "database"
    public string Title { get; set; }
    public string Text { get; set; }          // optional body text → paragraph blocks

    // Raw JSON passthrough for advanced callers (Notion's property/filter schemas vary per DB)
    public string PropertiesJson { get; set; }
    public string ChildrenJson { get; set; }
    public string FilterJson { get; set; }
    public string SortsJson { get; set; }
}
