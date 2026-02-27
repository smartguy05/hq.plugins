using HQ.Models.Interfaces;

namespace HQ.Plugins.Telegram.Models;

public class ServiceRequest: IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }
    public string ChatId { get; set; }
    public string MessageText { get; set; }
}