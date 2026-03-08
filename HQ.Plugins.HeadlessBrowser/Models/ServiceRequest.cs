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
}
