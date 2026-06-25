using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Maps.Models;

namespace HQ.Plugins.Maps;

/// <summary>Tool surface for Google Maps Platform (directions, travel time, places, geocoding). Read-only.</summary>
public class MapsService
{
    private readonly LogDelegate _logger;

    public MapsService(LogDelegate logger) => _logger = logger;

    /// <summary>Normalize a travel mode to a Maps API value. Defaults to driving.</summary>
    public static string NormalizeMode(string mode) =>
        (mode ?? "").Trim().ToLowerInvariant() switch
        {
            "walking" or "walk" or "foot" => "walking",
            "bicycling" or "bicycle" or "bike" or "cycling" => "bicycling",
            "transit" or "public" or "public_transport" => "transit",
            _ => "driving"
        };

    private static string Q(string value) => Uri.EscapeDataString(value ?? "");

    [Display(Name = MapsMethods.GetDirections)]
    [Description("Get turn-by-turn directions between an origin and destination.")]
    [Parameters(typeof(GetDirectionsArgs))]
    public Task<object> GetDirections(ServiceConfig config, GetDirectionsArgs r) =>
        Guard(async () =>
        {
            using var client = new MapsClient(config.ApiKey);
            var doc = await client.GetAsync($"/directions/json?origin={Q(r.Origin)}&destination={Q(r.Destination)}&mode={NormalizeMode(r.Mode)}");
            return Result(doc, "routes", "Routes");
        });

    [Display(Name = MapsMethods.GetTravelTime)]
    [Description("Get travel distance and duration between one or more origins and destinations.")]
    [Parameters(typeof(GetTravelTimeArgs))]
    public Task<object> GetTravelTime(ServiceConfig config, GetTravelTimeArgs r) =>
        Guard(async () =>
        {
            using var client = new MapsClient(config.ApiKey);
            var doc = await client.GetAsync($"/distancematrix/json?origins={Q(r.Origins)}&destinations={Q(r.Destinations)}&mode={NormalizeMode(r.Mode)}");
            return Result(doc, "rows", "Rows");
        });

    [Display(Name = MapsMethods.SearchPlaces)]
    [Description("Search for places (businesses, landmarks, addresses) by free-text query, optionally biased to a location.")]
    [Parameters(typeof(SearchPlacesArgs))]
    public Task<object> SearchPlaces(ServiceConfig config, SearchPlacesArgs r) =>
        Guard(async () =>
        {
            using var client = new MapsClient(config.ApiKey);
            var path = $"/place/textsearch/json?query={Q(r.Query)}";
            if (!string.IsNullOrWhiteSpace(r.Location)) path += $"&location={Q(r.Location)}";
            if (r.Radius.HasValue) path += $"&radius={r.Radius.Value}";
            var doc = await client.GetAsync(path);
            return Result(doc, "results", "Results");
        });

    [Display(Name = MapsMethods.GetPlaceDetails)]
    [Description("Get details for a place by its place_id (from search_places).")]
    [Parameters(typeof(GetPlaceDetailsArgs))]
    public Task<object> GetPlaceDetails(ServiceConfig config, GetPlaceDetailsArgs r) =>
        Guard(async () =>
        {
            using var client = new MapsClient(config.ApiKey);
            var doc = await client.GetAsync($"/place/details/json?place_id={Q(r.PlaceId)}");
            return Result(doc, "result", "Result");
        });

    [Display(Name = MapsMethods.GeocodeAddress)]
    [Description("Convert an address into coordinates (and a normalized address).")]
    [Parameters(typeof(GeocodeAddressArgs))]
    public Task<object> GeocodeAddress(ServiceConfig config, GeocodeAddressArgs r) =>
        Guard(async () =>
        {
            using var client = new MapsClient(config.ApiKey);
            var doc = await client.GetAsync($"/geocode/json?address={Q(r.Address)}");
            return Result(doc, "results", "Results");
        });

    /// <summary>Maps web-services return HTTP 200 with a logical "status"; surface failures explicitly.</summary>
    private static object Result(JsonElement doc, string dataProp, string outName)
    {
        var status = doc.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (status is not null && status != "OK" && status != "ZERO_RESULTS")
        {
            var msg = doc.TryGetProperty("error_message", out var em) ? em.GetString() : status;
            return new { Success = false, Status = status, Error = msg };
        }
        object data = doc.TryGetProperty(dataProp, out var el) ? el : (object)doc;
        return new Dictionary<string, object> { ["Success"] = true, ["Status"] = status ?? "OK", [outName] = data };
    }

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Maps operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
