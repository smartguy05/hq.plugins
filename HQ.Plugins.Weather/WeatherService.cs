using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Weather.Models;

namespace HQ.Plugins.Weather;

/// <summary>
/// Tool surface for OpenWeatherMap. Locations may be given as a place name (geocoded) or as
/// explicit lat/lon. Read-only.
/// </summary>
public class WeatherService
{
    private readonly LogDelegate _logger;

    public WeatherService(LogDelegate logger) => _logger = logger;

    /// <summary>Normalize a units string to an OpenWeatherMap value. Defaults to metric.</summary>
    public static string NormalizeUnits(string units) =>
        (units ?? "").Trim().ToLowerInvariant() switch
        {
            "imperial" or "f" or "fahrenheit" => "imperial",
            "standard" or "kelvin" or "k" => "standard",
            _ => "metric"
        };

    private string Units(ServiceConfig config, ServiceRequest r) =>
        NormalizeUnits(!string.IsNullOrWhiteSpace(r.Units) ? r.Units : config.DefaultUnits);

    private async Task<(double Lat, double Lon, string Label)> Resolve(WeatherClient client, ServiceRequest r)
    {
        if (r.Lat.HasValue && r.Lon.HasValue)
            return (r.Lat.Value, r.Lon.Value, $"{r.Lat.Value},{r.Lon.Value}");
        if (string.IsNullOrWhiteSpace(r.City))
            throw new InvalidOperationException("Provide either a city/place name or explicit lat and lon.");
        var hit = await client.GeocodeAsync(r.City)
                  ?? throw new InvalidOperationException($"Could not geocode location '{r.City}'.");
        return hit;
    }

    [Display(Name = WeatherMethods.GetCurrentWeather)]
    [Description("Get current weather conditions for a place name or coordinates.")]
    [Parameters("""{"type":"object","properties":{"city":{"type":"string","description":"Place name to geocode, e.g. 'Austin, TX'"},"lat":{"type":"number","description":"Latitude (use with lon instead of city)"},"lon":{"type":"number","description":"Longitude"},"units":{"type":"string","description":"metric | imperial | standard"}},"required":[]}""")]
    public Task<object> GetCurrentWeather(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new WeatherClient(config.ApiKey);
            var (lat, lon, label) = await Resolve(client, r);
            var units = Units(config, r);
            var doc = await client.OneCallAsync(lat, lon, units, "minutely,hourly,daily,alerts");
            return new { Success = true, Location = label, Units = units, Current = Prop(doc, "current") };
        });

    [Display(Name = WeatherMethods.GetForecast)]
    [Description("Get a multi-day daily forecast for a place name or coordinates.")]
    [Parameters("""{"type":"object","properties":{"city":{"type":"string","description":"Place name to geocode"},"lat":{"type":"number"},"lon":{"type":"number"},"units":{"type":"string","description":"metric | imperial | standard"},"days":{"type":"integer","description":"Number of forecast days, 1-8 (default 5)"}},"required":[]}""")]
    public Task<object> GetForecast(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new WeatherClient(config.ApiKey);
            var (lat, lon, label) = await Resolve(client, r);
            var units = Units(config, r);
            var doc = await client.OneCallAsync(lat, lon, units, "minutely,hourly,alerts");
            var days = Math.Clamp(r.Days ?? 5, 1, 8);
            object daily = doc.TryGetProperty("daily", out var d) && d.ValueKind == JsonValueKind.Array
                ? d.EnumerateArray().Take(days).ToArray()
                : Array.Empty<object>();
            return new { Success = true, Location = label, Units = units, Days = days, Daily = daily };
        });

    [Display(Name = WeatherMethods.GetWeatherAlerts)]
    [Description("Get active government weather alerts for a place name or coordinates.")]
    [Parameters("""{"type":"object","properties":{"city":{"type":"string","description":"Place name to geocode"},"lat":{"type":"number"},"lon":{"type":"number"}},"required":[]}""")]
    public Task<object> GetWeatherAlerts(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new WeatherClient(config.ApiKey);
            var (lat, lon, label) = await Resolve(client, r);
            var doc = await client.OneCallAsync(lat, lon, Units(config, r), "minutely,hourly,daily,current");
            object alerts = doc.TryGetProperty("alerts", out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().ToArray()
                : Array.Empty<object>();
            return new { Success = true, Location = label, Alerts = alerts };
        });

    private static object Prop(JsonElement doc, string name) =>
        doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty(name, out var el) ? el : doc;

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Weather operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
