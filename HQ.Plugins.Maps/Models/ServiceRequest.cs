using HQ.Models.Interfaces;

namespace HQ.Plugins.Maps.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Directions
    public string Origin { get; set; }
    public string Destination { get; set; }

    // Travel time (distance matrix) — comma-separated lists or single values
    public string Origins { get; set; }
    public string Destinations { get; set; }

    // driving | walking | bicycling | transit (default driving)
    public string Mode { get; set; }

    // Places text search
    public string Query { get; set; }
    public string Location { get; set; }   // "lat,lng" bias for search
    public int? Radius { get; set; }        // meters

    // Place details
    public string PlaceId { get; set; }

    // Geocoding
    public string Address { get; set; }
}
