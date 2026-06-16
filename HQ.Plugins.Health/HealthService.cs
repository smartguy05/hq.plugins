using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Health.Models;

namespace HQ.Plugins.Health;

/// <summary>Read-only health/fitness tools backed by the Terra wearables aggregator.</summary>
public class HealthService
{
    private readonly LogDelegate _logger;

    public HealthService(LogDelegate logger) => _logger = logger;

    /// <summary>Format a date for Terra (YYYY-MM-DD), falling back to a default.</summary>
    public static string Date(string value, DateTime fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback.ToString("yyyy-MM-dd") : value.Trim();

    /// <summary>Build a Terra data-endpoint path for a resource and window.</summary>
    public static string DataPath(string resource, string userId, string start, string end) =>
        $"/{resource}?user_id={Uri.EscapeDataString(userId ?? "")}&start_date={start}&end_date={end}";

    [Display(Name = HealthMethods.ListUsers)]
    [Description("List the wearable accounts (Terra users) connected to this app.")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public Task<object> ListUsers(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync("/subscriptions");
            return new { Success = true, Users = Prop(doc, "users") };
        });

    [Display(Name = HealthMethods.GetSleep)]
    [Description("Get sleep sessions (duration, stages, efficiency) for a connected user over a date range.")]
    [Parameters("""{"type":"object","properties":{"userId":{"type":"string"},"startDate":{"type":"string","description":"YYYY-MM-DD (default 7 days ago)"},"endDate":{"type":"string","description":"YYYY-MM-DD (default today)"}},"required":["userId"]}""")]
    public Task<object> GetSleep(ServiceConfig config, ServiceRequest r) => GetData(config, r, "sleep");

    [Display(Name = HealthMethods.GetActivity)]
    [Description("Get workouts/activity sessions for a connected user over a date range.")]
    [Parameters("""{"type":"object","properties":{"userId":{"type":"string"},"startDate":{"type":"string","description":"YYYY-MM-DD (default 7 days ago)"},"endDate":{"type":"string","description":"YYYY-MM-DD (default today)"}},"required":["userId"]}""")]
    public Task<object> GetActivity(ServiceConfig config, ServiceRequest r) => GetData(config, r, "activity");

    [Display(Name = HealthMethods.GetDaily)]
    [Description("Get daily summaries (steps, calories, heart rate, stress) for a connected user over a date range.")]
    [Parameters("""{"type":"object","properties":{"userId":{"type":"string"},"startDate":{"type":"string","description":"YYYY-MM-DD (default 7 days ago)"},"endDate":{"type":"string","description":"YYYY-MM-DD (default today)"}},"required":["userId"]}""")]
    public Task<object> GetDaily(ServiceConfig config, ServiceRequest r) => GetData(config, r, "daily");

    [Display(Name = HealthMethods.GetBody)]
    [Description("Get body measurements (weight, body fat, glucose, blood pressure) for a connected user over a date range.")]
    [Parameters("""{"type":"object","properties":{"userId":{"type":"string"},"startDate":{"type":"string","description":"YYYY-MM-DD (default 7 days ago)"},"endDate":{"type":"string","description":"YYYY-MM-DD (default today)"}},"required":["userId"]}""")]
    public Task<object> GetBody(ServiceConfig config, ServiceRequest r) => GetData(config, r, "body");

    private Task<object> GetData(ServiceConfig config, ServiceRequest r, string resource) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var now = DateTime.UtcNow;
            var path = DataPath(resource, r.UserId, Date(r.StartDate, now.AddDays(-7)), Date(r.EndDate, now));
            var doc = await client.GetAsync(path);
            return new { Success = true, Type = resource, Data = Prop(doc, "data") };
        });

    private static TerraClient Client(ServiceConfig config) => new(config.DevId, config.ApiKey);

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
            await _logger(LogLevel.Error, $"Health operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
