using HQ.Models.Interfaces;

namespace HQ.Plugins.Weather.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Location: provide either a free-text place (geocoded) or explicit coordinates.
    public string City { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }

    // metric | imperial | standard (falls back to config.DefaultUnits, then metric)
    public string Units { get; set; }

    // For get_forecast: number of forecast days to return (1-8, default 5)
    public int? Days { get; set; }
}
