using HQ.Models.Interfaces;

namespace HQ.Plugins.Plaid.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Optional override of the configured item access_token (for multi-item setups)
    public string AccessToken { get; set; }

    // Transactions window (YYYY-MM-DD); defaults to the last 30 days
    public string StartDate { get; set; }
    public string EndDate { get; set; }
    public int? Count { get; set; }
    public int? Offset { get; set; }
}
