using HQ.Models.Interfaces;

namespace HQ.Plugins.JobBoard.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    public string Query { get; set; }
    public string Location { get; set; }
    public string JobType { get; set; }
    public string Source { get; set; }
    public int? MaxResults { get; set; } = 10;
    public string MinSalary { get; set; }
    public string Skills { get; set; }
    public string PostedWithin { get; set; }

    public string JobId { get; set; }
    public string ApplicationId { get; set; }
    public string Status { get; set; }
    public string Notes { get; set; }
}
