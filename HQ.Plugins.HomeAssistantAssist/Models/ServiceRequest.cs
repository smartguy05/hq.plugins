using HQ.Models.Interfaces;

namespace HQ.Plugins.HomeAssistantVoice.Models;

public class ServiceRequest: IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }
    public string Query { get; set; }
}