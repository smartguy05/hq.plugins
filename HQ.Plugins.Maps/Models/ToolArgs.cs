using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.Maps.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. None of the Maps tools support confirmation or read unadvertised fields, so no
/// <c>[Injected]</c> / <see cref="HQ.Models.Interfaces.IPluginServiceRequest"/> members are needed.
/// </summary>

public class GetDirectionsArgs
{
    [Required, Description("Start address or 'lat,lng'")]
    public string Origin { get; set; }

    [Required, Description("End address or 'lat,lng'")]
    public string Destination { get; set; }

    [Description("driving | walking | bicycling | transit")]
    public string Mode { get; set; }
}

public class GetTravelTimeArgs
{
    [Required, Description("One or more origins, '|'-separated (address or 'lat,lng')")]
    public string Origins { get; set; }

    [Required, Description("One or more destinations, '|'-separated")]
    public string Destinations { get; set; }

    [Description("driving | walking | bicycling | transit")]
    public string Mode { get; set; }
}

public class SearchPlacesArgs
{
    [Required, Description("Free-text query, e.g. 'coffee near downtown Austin'")]
    public string Query { get; set; }

    [Description("Optional 'lat,lng' bias")]
    public string Location { get; set; }

    [Description("Optional bias radius in meters")]
    public int? Radius { get; set; }
}

public class GetPlaceDetailsArgs
{
    [Required, Description("Google place_id")]
    public string PlaceId { get; set; }
}

public class GeocodeAddressArgs
{
    [Required, Description("Street address or place name")]
    public string Address { get; set; }
}
