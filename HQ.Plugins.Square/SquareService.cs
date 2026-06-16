using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Square.Models;

namespace HQ.Plugins.Square;

/// <summary>
/// Tool surface for Square: locations, catalog/inventory, customers, payments/orders, and
/// bookings/appointments. Booking writes notify customers, so they route through confirmation.
/// </summary>
public class SquareService
{
    private const string PluginName = "Square";
    private readonly LogDelegate _logger;
    private readonly INotificationService _notificationService;

    public SquareService(INotificationService notificationService, LogDelegate logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    private static SquareClient Client(ServiceConfig c) => new(c.AccessToken, c.UseSandbox);

    private static string Location(ServiceConfig config, ServiceRequest r)
    {
        var id = string.IsNullOrWhiteSpace(r.LocationId) ? config.DefaultLocationId : r.LocationId;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("locationId is required (or set DefaultLocationId in the plugin config).");
        return id;
    }

    [Display(Name = SquareMethods.ListLocations)]
    [Description("List the business's Square locations (and their IDs).")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public Task<object> ListLocations(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync("/locations");
            return new { Success = true, Locations = Prop(doc, "locations") };
        });

    [Display(Name = SquareMethods.ListCatalogItems)]
    [Description("List catalog items (products/services).")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public Task<object> ListCatalogItems(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync("/catalog/list?types=ITEM");
            return new { Success = true, Items = Prop(doc, "objects") };
        });

    [Display(Name = SquareMethods.GetInventoryCounts)]
    [Description("Get inventory counts for a catalog item variation.")]
    [Parameters("""{"type":"object","properties":{"catalogObjectId":{"type":"string","description":"Catalog item variation ID"},"locationId":{"type":"string"}},"required":["catalogObjectId"]}""")]
    public Task<object> GetInventoryCounts(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var body = new Dictionary<string, object> { ["catalog_object_ids"] = new[] { r.CatalogObjectId } };
            var locId = string.IsNullOrWhiteSpace(r.LocationId) ? config.DefaultLocationId : r.LocationId;
            if (!string.IsNullOrWhiteSpace(locId)) body["location_ids"] = new[] { locId };
            var doc = await client.PostAsync("/inventory/counts/batch-retrieve", body);
            return new { Success = true, Counts = Prop(doc, "counts") };
        });

    [Display(Name = SquareMethods.ListCustomers)]
    [Description("List customers.")]
    [Parameters("""{"type":"object","properties":{"limit":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> ListCustomers(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/customers?limit={r.Limit ?? 50}");
            return new { Success = true, Customers = Prop(doc, "customers") };
        });

    [Display(Name = SquareMethods.SearchCustomers)]
    [Description("Search customers by email (fuzzy).")]
    [Parameters("""{"type":"object","properties":{"query":{"type":"string","description":"Email text to fuzzy-match"}},"required":["query"]}""")]
    public Task<object> SearchCustomers(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var body = new { query = new { filter = new { email_address = new { fuzzy = r.Query } } } };
            var doc = await client.PostAsync("/customers/search", body);
            return new { Success = true, Customers = Prop(doc, "customers") };
        });

    [Display(Name = SquareMethods.CreateCustomer)]
    [Description("Create a new customer.")]
    [Parameters("""{"type":"object","properties":{"givenName":{"type":"string"},"familyName":{"type":"string"},"email":{"type":"string"},"phone":{"type":"string"}},"required":[]}""")]
    public Task<object> CreateCustomer(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var body = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(r.GivenName)) body["given_name"] = r.GivenName;
            if (!string.IsNullOrWhiteSpace(r.FamilyName)) body["family_name"] = r.FamilyName;
            if (!string.IsNullOrWhiteSpace(r.Email)) body["email_address"] = r.Email;
            if (!string.IsNullOrWhiteSpace(r.Phone)) body["phone_number"] = r.Phone;
            if (body.Count == 0) return new { Success = false, Error = "Provide at least one customer field." };
            var doc = await client.PostAsync("/customers", body);
            return new { Success = true, Customer = Prop(doc, "customer") };
        });

    [Display(Name = SquareMethods.ListPayments)]
    [Description("List payments for a location.")]
    [Parameters("""{"type":"object","properties":{"locationId":{"type":"string"},"limit":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> ListPayments(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/payments?location_id={Location(config, r)}&limit={r.Limit ?? 50}");
            return new { Success = true, Payments = Prop(doc, "payments") };
        });

    [Display(Name = SquareMethods.ListOrders)]
    [Description("List recent orders for a location.")]
    [Parameters("""{"type":"object","properties":{"locationId":{"type":"string"}},"required":[]}""")]
    public Task<object> ListOrders(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.PostAsync("/orders/search", new { location_ids = new[] { Location(config, r) } });
            return new { Success = true, Orders = Prop(doc, "orders") };
        });

    [Display(Name = SquareMethods.ListBookings)]
    [Description("List bookings (appointments) for a location.")]
    [Parameters("""{"type":"object","properties":{"locationId":{"type":"string"},"limit":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> ListBookings(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/bookings?location_id={Location(config, r)}&limit={r.Limit ?? 50}");
            return new { Success = true, Bookings = Prop(doc, "bookings") };
        });

    [Display(Name = SquareMethods.SearchAvailability)]
    [Description("Search available appointment slots for a service variation within a time window (defaults to the next 7 days).")]
    [Parameters("""{"type":"object","properties":{"locationId":{"type":"string"},"serviceVariationId":{"type":"string","description":"Catalog service variation ID"},"startAt":{"type":"string","description":"RFC3339 window start (default now)"}},"required":["serviceVariationId"]}""")]
    public Task<object> SearchAvailability(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var start = string.IsNullOrWhiteSpace(r.StartAt) ? DateTime.UtcNow : DateTime.Parse(r.StartAt).ToUniversalTime();
            var body = new
            {
                query = new
                {
                    filter = new
                    {
                        start_at_range = new
                        {
                            start_at = start.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            end_at = start.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ssZ")
                        },
                        location_id = Location(config, r),
                        segment_filters = new[] { new { service_variation_id = r.ServiceVariationId } }
                    }
                }
            };
            var doc = await client.PostAsync("/bookings/availability/search", body);
            return new { Success = true, Availabilities = Prop(doc, "availabilities") };
        });

    [Display(Name = SquareMethods.CreateBooking)]
    [Description("Book an appointment for a customer. Notifies the customer.")]
    [Parameters("""{"type":"object","properties":{"locationId":{"type":"string"},"customerId":{"type":"string"},"serviceVariationId":{"type":"string"},"teamMemberId":{"type":"string"},"startAt":{"type":"string","description":"RFC3339 appointment start time"}},"required":["customerId","serviceVariationId","teamMemberId","startAt"]}""")]
    [SupportsConfirmation]
    public Task<object> CreateBooking(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Confirm(config, r, "Book this appointment?", $"Customer {r.CustomerId} at {r.StartAt}", async () =>
        {
            using var client = Client(config);
            var body = new
            {
                booking = new
                {
                    location_id = Location(config, r),
                    start_at = r.StartAt,
                    customer_id = r.CustomerId,
                    appointment_segments = new[]
                    {
                        new { team_member_id = r.TeamMemberId, service_variation_id = r.ServiceVariationId }
                    }
                }
            };
            var doc = await client.PostAsync("/bookings", body);
            return new { Success = true, Booking = Prop(doc, "booking") };
        }));

    [Display(Name = SquareMethods.CancelBooking)]
    [Description("Cancel a booking. Notifies the customer.")]
    [Parameters("""{"type":"object","properties":{"bookingId":{"type":"string"}},"required":["bookingId"]}""")]
    [SupportsConfirmation]
    public Task<object> CancelBooking(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Confirm(config, r, "Cancel this booking?", $"Booking {r.BookingId}", async () =>
        {
            using var client = Client(config);
            var doc = await client.PostAsync($"/bookings/{r.BookingId}/cancel", new { });
            return new { Success = true, Booking = Prop(doc, "booking") };
        }));

    // ───────────────────────────── Plumbing ─────────────────────────────

    private static object Prop(JsonElement doc, string name) =>
        doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty(name, out var el) ? el : doc;

    private async Task<object> Confirm(ServiceConfig config, ServiceRequest request, string message, string content, Func<Task<object>> execute)
    {
        if (config.RequiresConfirmation && _notificationService != null)
        {
            if (string.IsNullOrWhiteSpace(request.ConfirmationId))
            {
                var confirmation = new Confirmation
                {
                    ConfirmationMessage = message,
                    Content = content,
                    Options = new Dictionary<string, bool> { { "Yes", true }, { "No", false } },
                    Id = Guid.NewGuid()
                };
                return await _notificationService.RequestConfirmation(PluginName, confirmation, request);
            }

            if (!_notificationService.DoesConfirmationExist(Guid.Parse(request.ConfirmationId), out _))
                return new { Success = false, Error = "Action was not confirmed." };
        }

        return await execute();
    }

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Square operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
