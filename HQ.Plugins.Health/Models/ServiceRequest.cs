using HQ.Models.Interfaces;

namespace HQ.Plugins.Health.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // The Terra user id (one connected wearable account)
    public string UserId { get; set; }

    // Data window (YYYY-MM-DD); defaults to the last 7 days
    public string StartDate { get; set; }
    public string EndDate { get; set; }
}
