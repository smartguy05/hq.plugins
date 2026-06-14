using HQ.Models.Interfaces;

namespace HQ.Plugins.Calendly.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Calendly addresses resources by full URI. These accept either a full URI or a bare UUID.
    public string EventTypeUri { get; set; }   // for create_scheduling_link
    public string EventUri { get; set; }       // a scheduled event URI/UUID
    public string UserUri { get; set; }        // override the current user
    public string OrganizationUri { get; set; }

    // Filtering / paging
    public string Status { get; set; }         // active | canceled (scheduled events)
    public int? Count { get; set; }

    // Cancellation
    public string Reason { get; set; }
}
