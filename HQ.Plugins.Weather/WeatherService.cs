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

    private string Units(ServiceConfig config, string units) =>
        NormalizeUnits(!string.IsNullOrWhiteSpace(units) ? units : config.DefaultUnits);

    private async Task<(double Lat, double Lon, string Label)> Resolve(WeatherClient client, string city, double? lat, double? lon)
    {
        if (lat.HasValue && lon.HasValue)
            return (lat.Value, lon.Value, $"{lat.Value},{lon.Value}");
        if (string.IsNullOrWhiteSpace(city))
            throw new InvalidOperationException("Provide either a city/place name or explicit lat and lon.");
        var hit = await client.GeocodeAsync(city)
                  ?? throw new InvalidOperationException($"Could not geocode location '{city}'.");
        return hit;
    }

    [Display(Name = WeatherMethods.GetCurrentWeather)]
    [Description("Get current weather conditions for a place name or coordinates.")]
    [Parameters(typeof(GetCurrentWeatherArgs))]
    public Task<object> GetCurrentWeather(ServiceConfig config, GetCurrentWeatherArgs r) =>
        Guard(async () =>
        {
            using var client = new WeatherClient(config.ApiKey);
            var (lat, lon, label) = await Resolve(client, r.City, r.Lat, r.Lon);
            var units = Units(config, r.Units);
            var doc = await client.OneCallAsync(lat, lon, units, "minutely,hourly,daily,alerts");
            return new { Success = true, Location = label, Units = units, Current = Prop(doc, "current") };
        });

    [Display(Name = WeatherMethods.GetForecast)]
    [Description("Get a multi-day daily forecast for a place name or coordinates.")]
    [Parameters(typeof(GetForecastArgs))]
    public Task<object> GetForecast(ServiceConfig config, GetForecastArgs r) =>
        Guard(async () =>
        {
            using var client = new WeatherClient(config.ApiKey);
            var (lat, lon, label) = await Resolve(client, r.City, r.Lat, r.Lon);
            var units = Units(config, r.Units);
            var doc = await client.OneCallAsync(lat, lon, units, "minutely,hourly,alerts");
            var days = Math.Clamp(r.Days ?? 5, 1, 8);
            object daily = doc.TryGetProperty("daily", out var d) && d.ValueKind == JsonValueKind.Array
                ? d.EnumerateArray().Take(days).ToArray()
                : Array.Empty<object>();
            return new { Success = true, Location = label, Units = units, Days = days, Daily = daily };
        });

    [Display(Name = WeatherMethods.GetWeatherAlerts)]
    [Description("Get active government weather alerts for a place name or coordinates.")]
    [Parameters(typeof(GetWeatherAlertsArgs))]
    public Task<object> GetWeatherAlerts(ServiceConfig config, GetWeatherAlertsArgs r) =>
        Guard(async () =>
        {
            using var client = new WeatherClient(config.ApiKey);
            var (lat, lon, label) = await Resolve(client, r.City, r.Lat, r.Lon);
            var doc = await client.OneCallAsync(lat, lon, Units(config, null), "minutely,hourly,daily,current");
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
