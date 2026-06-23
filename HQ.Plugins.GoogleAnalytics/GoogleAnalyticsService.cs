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

    private static string Property(ServiceConfig config, string propertyId)
    {
        var id = string.IsNullOrWhiteSpace(propertyId) ? config.DefaultPropertyId : propertyId;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("propertyId is required (or set DefaultPropertyId in the plugin config).");
        return id;
    }

    [Display(Name = GoogleAnalyticsMethods.RunReport)]
    [Description("Run a GA4 report over a date range. Dimensions/metrics are comma-separated, e.g. dimensions='date,country', metrics='activeUsers,sessions'.")]
    [Parameters(typeof(RunReportArgs))]
    public Task<object> RunReport(ServiceConfig config, RunReportArgs request) =>
        Guard(async () =>
        {
            using var client = new GaClient(config);
            var body = new
            {
                dimensions = GaClient.NameList(request.Dimensions),
                metrics = GaClient.NameList(request.Metrics),
                dateRanges = new[]
                {
                    new
                    {
                        startDate = string.IsNullOrWhiteSpace(request.StartDate) ? "28daysAgo" : request.StartDate,
                        endDate = string.IsNullOrWhiteSpace(request.EndDate) ? "today" : request.EndDate
                    }
                },
                limit = request.Limit ?? 100
            };
            var doc = await client.PostAsync($"/properties/{Property(config, request.PropertyId)}:runReport", body);
            return new { Success = true, Report = doc };
        });

    [Display(Name = GoogleAnalyticsMethods.RunRealtimeReport)]
    [Description("Run a GA4 realtime report (last 30 minutes). Dimensions/metrics are comma-separated.")]
    [Parameters(typeof(RunRealtimeReportArgs))]
    public Task<object> RunRealtimeReport(ServiceConfig config, RunRealtimeReportArgs request) =>
        Guard(async () =>
        {
            using var client = new GaClient(config);
            var body = new
            {
                dimensions = GaClient.NameList(request.Dimensions),
                metrics = GaClient.NameList(request.Metrics),
                limit = request.Limit ?? 100
            };
            var doc = await client.PostAsync($"/properties/{Property(config, request.PropertyId)}:runRealtimeReport", body);
            return new { Success = true, Report = doc };
        });

    [Display(Name = GoogleAnalyticsMethods.GetMetadata)]
    [Description("List the dimensions and metrics available for a GA4 property.")]
    [Parameters(typeof(GetMetadataArgs))]
    public Task<object> GetMetadata(ServiceConfig config, GetMetadataArgs request) =>
        Guard(async () =>
        {
            using var client = new GaClient(config);
            var doc = await client.GetAsync($"/properties/{Property(config, request.PropertyId)}/metadata");
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
