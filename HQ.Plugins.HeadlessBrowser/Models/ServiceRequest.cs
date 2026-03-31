using HQ.Models.Interfaces;

namespace HQ.Plugins.HeadlessBrowser.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    public string Url { get; set; }
    public string Selector { get; set; }
    public string Value { get; set; }
    public string ContentType { get; set; }
    public int? MaxLength { get; set; }
    public string Script { get; set; }
    public string FileName { get; set; }
    public bool? FullPage { get; set; }
    public string ElementType { get; set; }

    // Phase 1: AriaSnapshot
    public string Format { get; set; }

    // Phase 2: Ref-based targeting
    public string Ref { get; set; }

    // Phase 3: Two-tier retrieval
    public string Query { get; set; }

    // Phase 5: Task-scoped filtering
    public string TaskHint { get; set; }

    // Phase 6: Diff mode
    public bool? DiffMode { get; set; }
}
