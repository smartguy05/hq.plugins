using HQ.Models.Interfaces;

namespace HQ.Plugins.Ramp.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    public string TransactionId { get; set; }
    public string CardId { get; set; }
    public string UserId { get; set; }

    // Paging / filters
    public int? PageSize { get; set; }
    public string FromDate { get; set; }   // ISO date for transaction filtering
    public string ToDate { get; set; }
}
