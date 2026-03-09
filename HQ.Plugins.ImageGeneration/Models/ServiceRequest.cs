using HQ.Models.Interfaces;

namespace HQ.Plugins.ImageGeneration.Models;

public class ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }
    public string Prompt { get; set; }
    public string AspectRatio { get; set; } = "1:1";
    public string Resolution { get; set; } = "1K";
    public string ReferenceImage { get; set; }
    public string OutputFileName { get; set; }
}
