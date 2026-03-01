using HQ.Models.Interfaces;

namespace HQ.Plugins.Slack.Models;

public class ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }
    public string ChannelId { get; set; }
    public string MessageText { get; set; }
    public string FileContent { get; set; }
    public string FileName { get; set; }
    public string FileType { get; set; }
    public string FileId { get; set; }
}
