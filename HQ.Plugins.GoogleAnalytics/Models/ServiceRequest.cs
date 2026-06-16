using HQ.Models.Interfaces;

namespace HQ.Plugins.GoogleAnalytics.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    public string PropertyId { get; set; }   // GA4 property (numeric); falls back to config default

    // Report shaping — comma-separated lists keep the LLM-facing schema simple.
    public string Dimensions { get; set; }   // e.g. "date,country"
    public string Metrics { get; set; }      // e.g. "activeUsers,sessions"
    public string StartDate { get; set; }    // YYYY-MM-DD or 'NdaysAgo' / 'today'
    public string EndDate { get; set; }
    public int? Limit { get; set; }
}
