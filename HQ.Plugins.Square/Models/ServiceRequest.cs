using HQ.Models.Interfaces;

namespace HQ.Plugins.Square.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    public string LocationId { get; set; }
    public string CustomerId { get; set; }
    public string BookingId { get; set; }

    // Customer fields
    public string GivenName { get; set; }
    public string FamilyName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }

    // Search
    public string Query { get; set; }
    public int? Limit { get; set; }

    // Catalog / inventory
    public string CatalogObjectId { get; set; }   // variation id for inventory counts

    // Bookings
    public string ServiceVariationId { get; set; }
    public string TeamMemberId { get; set; }
    public string StartAt { get; set; }           // RFC3339 timestamp
    public string CancelReason { get; set; }
}
