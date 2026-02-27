using HQ.Models.Interfaces;

namespace HQ.Plugins.SupportChannelKb.Models;

public record ServiceRequest: IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }
    
    public string SearchCriteria { get; set; }
    public string SupportChannel { get; set; }
    public string Description { get; set; }
    public string NewInformation { get; set; }
    public List<Dictionary<string, string>> NewInformationMetaData { get; set; } = new();
}