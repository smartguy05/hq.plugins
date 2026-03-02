using HQ.Models.Interfaces;

namespace HQ.Plugins.ReportGenerator.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    public string Title { get; set; }
    public string Content { get; set; }
    public string Format { get; set; } = "html";
    public string FileName { get; set; }
    public string ReportId { get; set; }
}
