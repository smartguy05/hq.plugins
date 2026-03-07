using HQ.Models.Interfaces;

namespace HQ.Plugins.Teams.Models;

public class ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }
    // Teams-specific
    public string TeamId { get; set; }
    public string ChannelId { get; set; }
    public string ChatId { get; set; }
    public string MessageText { get; set; }
    public string FileContent { get; set; }     // Base64
    public string FileName { get; set; }
    public string FileType { get; set; }
    public string DriveItemId { get; set; }
}
