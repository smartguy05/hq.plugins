using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.GoogleAnalytics.Models;

namespace HQ.Plugins.GoogleAnalytics;

/// <summary>Tool surface for GA4 web analytics (read-only reporting via the GA4 Data API).</summary>
public class GoogleAnalyticsService
{
    private readonly LogDelegate _logger;

    public GoogleAnalyticsService(LogDelegate logger) => _logger = logger;

    private static string Property(ServiceConfig config, ServiceRequest r)
    {
        var id = string.IsNullOrWhiteSpace(r.PropertyId) ? config.DefaultPropertyId : r.PropertyId;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("propertyId is required (or set DefaultPropertyId in the plugin config).");
        return id;
    }

    [Display(Name = GoogleAnalyticsMethods.RunReport)]
    [Description("Run a GA4 report over a date range. Dimensions/metrics are comma-separated, e.g. dimensions='date,country', metrics='activeUsers,sessions'.")]
    [Parameters("""{"type":"object","properties":{"propertyId":{"type":"string","description":"GA4 property ID (numeric)"},"dimensions":{"type":"string","description":"Comma-separated dimension names"},"metrics":{"type":"string","description":"Comma-separated metric names"},"startDate":{"type":"string","description":"YYYY-MM-DD, or '7daysAgo'/'today'"},"endDate":{"type":"string","description":"YYYY-MM-DD, or 'today'"},"limit":{"type":"integer","description":"Max rows (default 100)"}},"required":["metrics"]}""")]
    public Task<object> RunReport(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new GaClient(config);
            var body = new
            {
                dimensions = GaClient.NameList(r.Dimensions),
                metrics = GaClient.NameList(r.Metrics),
                dateRanges = new[]
                {
                    new
                    {
                        startDate = string.IsNullOrWhiteSpace(r.StartDate) ? "28daysAgo" : r.StartDate,
                        endDate = string.IsNullOrWhiteSpace(r.EndDate) ? "today" : r.EndDate
                    }
                },
                limit = r.Limit ?? 100
            };
            var doc = await client.PostAsync($"/properties/{Property(config, r)}:runReport", body);
            return new { Success = true, Report = doc };
        });

    [Display(Name = GoogleAnalyticsMethods.RunRealtimeReport)]
    [Description("Run a GA4 realtime report (last 30 minutes). Dimensions/metrics are comma-separated.")]
    [Parameters("""{"type":"object","properties":{"propertyId":{"type":"string"},"dimensions":{"type":"string","description":"Comma-separated dimension names, e.g. 'country'"},"metrics":{"type":"string","description":"Comma-separated metric names, e.g. 'activeUsers'"},"limit":{"type":"integer","description":"Max rows (default 100)"}},"required":["metrics"]}""")]
    public Task<object> RunRealtimeReport(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new GaClient(config);
            var body = new
            {
                dimensions = GaClient.NameList(r.Dimensions),
                metrics = GaClient.NameList(r.Metrics),
                limit = r.Limit ?? 100
            };
            var doc = await client.PostAsync($"/properties/{Property(config, r)}:runRealtimeReport", body);
            return new { Success = true, Report = doc };
        });

    [Display(Name = GoogleAnalyticsMethods.GetMetadata)]
    [Description("List the dimensions and metrics available for a GA4 property.")]
    [Parameters("""{"type":"object","properties":{"propertyId":{"type":"string"}},"required":[]}""")]
    public Task<object> GetMetadata(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new GaClient(config);
            var doc = await client.GetAsync($"/properties/{Property(config, r)}/metadata");
            return new { Success = true, Metadata = doc };
        });

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Google Analytics operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
