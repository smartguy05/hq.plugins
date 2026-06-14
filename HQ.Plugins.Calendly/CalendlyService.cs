using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Calendly.Models;

namespace HQ.Plugins.Calendly;

/// <summary>
/// Tool surface for Calendly. Note: actual booking happens on Calendly's hosted page — this
/// surfaces event types, generates single-use scheduling links, and reads/cancels events.
/// </summary>
public class CalendlyService
{
    private readonly LogDelegate _logger;

    public CalendlyService(LogDelegate logger) => _logger = logger;

    /// <summary>Extracts the trailing UUID from a Calendly resource URI (or returns the input if already a UUID).</summary>
    public static string Uuid(string uriOrId)
    {
        if (string.IsNullOrWhiteSpace(uriOrId)) return uriOrId;
        var trimmed = uriOrId.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    private static async Task<(string userUri, string orgUri)> CurrentUser(CalendlyClient client)
    {
        var me = await client.GetAsync("/users/me");
        var resource = me.GetProperty("resource");
        return (resource.GetProperty("uri").GetString(),
                resource.GetProperty("current_organization").GetString());
    }

    [Display(Name = CalendlyMethods.GetCurrentUser)]
    [Description("Get the authenticated Calendly user (URI, name, email, scheduling URL, organization).")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public Task<object> GetCurrentUser(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new CalendlyClient(config.AccessToken);
            var me = await client.GetAsync("/users/me");
            return new { Success = true, User = me.GetProperty("resource") };
        });

    [Display(Name = CalendlyMethods.ListEventTypes)]
    [Description("List the bookable event types for the user.")]
    [Parameters("""{"type":"object","properties":{"userUri":{"type":"string","description":"User URI (defaults to the authenticated user)"},"count":{"type":"integer","description":"Max results (default 25)"}},"required":[]}""")]
    public Task<object> ListEventTypes(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new CalendlyClient(config.AccessToken);
            var userUri = r.UserUri;
            if (string.IsNullOrWhiteSpace(userUri)) (userUri, _) = await CurrentUser(client);
            var doc = await client.GetAsync($"/event_types?user={Uri.EscapeDataString(userUri)}&count={r.Count ?? 25}");
            return new { Success = true, EventTypes = Prop(doc, "collection") };
        });

    [Display(Name = CalendlyMethods.CreateSchedulingLink)]
    [Description("Create a single-use scheduling link for an event type that an invitee can use to book.")]
    [Parameters("""{"type":"object","properties":{"eventTypeUri":{"type":"string","description":"The event type URI to book against"}},"required":["eventTypeUri"]}""")]
    public Task<object> CreateSchedulingLink(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new CalendlyClient(config.AccessToken);
            var doc = await client.PostAsync("/scheduling_links", new
            {
                max_event_count = 1,
                owner = r.EventTypeUri,
                owner_type = "EventType"
            });
            var resource = doc.GetProperty("resource");
            return new { Success = true, BookingUrl = resource.GetProperty("booking_url").GetString() };
        });

    [Display(Name = CalendlyMethods.ListScheduledEvents)]
    [Description("List scheduled (booked) events for the user, optionally filtered by status.")]
    [Parameters("""{"type":"object","properties":{"userUri":{"type":"string","description":"User URI (defaults to the authenticated user)"},"status":{"type":"string","description":"active | canceled"},"count":{"type":"integer","description":"Max results (default 25)"}},"required":[]}""")]
    public Task<object> ListScheduledEvents(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new CalendlyClient(config.AccessToken);
            var userUri = r.UserUri;
            if (string.IsNullOrWhiteSpace(userUri)) (userUri, _) = await CurrentUser(client);
            var path = $"/scheduled_events?user={Uri.EscapeDataString(userUri)}&count={r.Count ?? 25}";
            if (!string.IsNullOrWhiteSpace(r.Status)) path += $"&status={Uri.EscapeDataString(r.Status)}";
            var doc = await client.GetAsync(path);
            return new { Success = true, Events = Prop(doc, "collection") };
        });

    [Display(Name = CalendlyMethods.GetScheduledEvent)]
    [Description("Get a single scheduled event by URI or UUID.")]
    [Parameters("""{"type":"object","properties":{"eventUri":{"type":"string","description":"Scheduled event URI or UUID"}},"required":["eventUri"]}""")]
    public Task<object> GetScheduledEvent(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new CalendlyClient(config.AccessToken);
            var doc = await client.GetAsync($"/scheduled_events/{Uuid(r.EventUri)}");
            return new { Success = true, Event = Prop(doc, "resource") };
        });

    [Display(Name = CalendlyMethods.ListEventInvitees)]
    [Description("List the invitees (attendees) of a scheduled event.")]
    [Parameters("""{"type":"object","properties":{"eventUri":{"type":"string","description":"Scheduled event URI or UUID"}},"required":["eventUri"]}""")]
    public Task<object> ListEventInvitees(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new CalendlyClient(config.AccessToken);
            var doc = await client.GetAsync($"/scheduled_events/{Uuid(r.EventUri)}/invitees");
            return new { Success = true, Invitees = Prop(doc, "collection") };
        });

    [Display(Name = CalendlyMethods.CancelEvent)]
    [Description("Cancel a scheduled event with an optional reason.")]
    [Parameters("""{"type":"object","properties":{"eventUri":{"type":"string","description":"Scheduled event URI or UUID"},"reason":{"type":"string","description":"Cancellation reason shown to the invitee"}},"required":["eventUri"]}""")]
    public Task<object> CancelEvent(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new CalendlyClient(config.AccessToken);
            var doc = await client.PostAsync($"/scheduled_events/{Uuid(r.EventUri)}/cancellation", new { reason = r.Reason ?? "" });
            return new { Success = true, Cancellation = Prop(doc, "resource") };
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
            await _logger(LogLevel.Error, $"Calendly operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
