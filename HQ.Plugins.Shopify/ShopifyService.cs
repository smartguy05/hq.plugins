using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Shopify.Models;

namespace HQ.Plugins.Shopify;

/// <summary>Tool surface for Shopify store management (products, orders, customers, inventory).</summary>
public class ShopifyService
{
    private const string PluginName = "Shopify";
    private readonly LogDelegate _logger;
    private readonly INotificationService _notificationService;

    public ShopifyService(INotificationService notificationService, LogDelegate logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    private static ShopifyClient Client(ServiceConfig c) => new(c.ShopDomain, c.AccessToken, string.IsNullOrWhiteSpace(c.ApiVersion) ? "2025-01" : c.ApiVersion);

    [Display(Name = ShopifyMethods.ListProducts)]
    [Description("List products in the store.")]
    [Parameters(typeof(ListProductsArgs))]
    public Task<object> ListProducts(ServiceConfig config, ListProductsArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/products.json?limit={r.Limit ?? 50}");
            return new { Success = true, Products = Prop(doc, "products") };
        });

    [Display(Name = ShopifyMethods.GetProduct)]
    [Description("Get a single product by ID.")]
    [Parameters(typeof(GetProductArgs))]
    public Task<object> GetProduct(ServiceConfig config, GetProductArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/products/{r.ProductId}.json");
            return new { Success = true, Product = Prop(doc, "product") };
        });

    [Display(Name = ShopifyMethods.CreateProduct)]
    [Description("Create a new product with a single default variant price.")]
    [Parameters(typeof(CreateProductArgs))]
    public Task<object> CreateProduct(ServiceConfig config, CreateProductArgs r) =>
        Guard(() => Confirm(config, r, "Create this product?", r.Title, async () =>
        {
            using var client = Client(config);
            var product = new Dictionary<string, object> { ["title"] = r.Title };
            if (!string.IsNullOrWhiteSpace(r.BodyHtml)) product["body_html"] = r.BodyHtml;
            if (!string.IsNullOrWhiteSpace(r.Vendor)) product["vendor"] = r.Vendor;
            if (!string.IsNullOrWhiteSpace(r.ProductType)) product["product_type"] = r.ProductType;
            if (r.Price.HasValue) product["variants"] = new[] { new { price = r.Price.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
            var doc = await client.PostAsync("/products.json", new { product });
            return new { Success = true, Product = Prop(doc, "product") };
        }));

    [Display(Name = ShopifyMethods.UpdateInventory)]
    [Description("Set the available inventory for an inventory item at a location.")]
    [Parameters(typeof(UpdateInventoryArgs))]
    public Task<object> UpdateInventory(ServiceConfig config, UpdateInventoryArgs r) =>
        Guard(() => Confirm(config, r, "Update inventory level?", $"Item {r.InventoryItemId} @ {r.LocationId} → {r.Available}", async () =>
        {
            using var client = Client(config);
            var doc = await client.PostAsync("/inventory_levels/set.json", new
            {
                location_id = r.LocationId,
                inventory_item_id = r.InventoryItemId,
                available = r.Available ?? 0
            });
            return new { Success = true, InventoryLevel = Prop(doc, "inventory_level") };
        }));

    [Display(Name = ShopifyMethods.ListOrders)]
    [Description("List orders (any status).")]
    [Parameters(typeof(ListOrdersArgs))]
    public Task<object> ListOrders(ServiceConfig config, ListOrdersArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/orders.json?status=any&limit={r.Limit ?? 50}");
            return new { Success = true, Orders = Prop(doc, "orders") };
        });

    [Display(Name = ShopifyMethods.GetOrder)]
    [Description("Get a single order by ID.")]
    [Parameters(typeof(GetOrderArgs))]
    public Task<object> GetOrder(ServiceConfig config, GetOrderArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/orders/{r.OrderId}.json");
            return new { Success = true, Order = Prop(doc, "order") };
        });

    [Display(Name = ShopifyMethods.FulfillOrder)]
    [Description("Fulfill all unfulfilled line items of an order, optionally notifying the customer.")]
    [Parameters(typeof(FulfillOrderArgs))]
    public Task<object> FulfillOrder(ServiceConfig config, FulfillOrderArgs r) =>
        Guard(() => Confirm(config, r, "Fulfill this order?", $"Order {r.OrderId}", async () =>
        {
            using var client = Client(config);
            // 2025 fulfillment flow: resolve the order's fulfillment orders, then create a fulfillment.
            var foDoc = await client.GetAsync($"/orders/{r.OrderId}/fulfillment_orders.json");
            var foIds = Prop(foDoc, "fulfillment_orders") is JsonElement arr && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Select(fo => fo.GetProperty("id").GetRawText()).ToList()
                : [];
            if (foIds.Count == 0) return new { Success = false, Error = "No fulfillment orders found for this order." };

            var lineItems = foIds.Select(id => new { fulfillment_order_id = JsonSerializer.Deserialize<object>(id) }).ToArray();
            var doc = await client.PostAsync("/fulfillments.json", new
            {
                fulfillment = new { line_items_by_fulfillment_order = lineItems, notify_customer = true }
            });
            return new { Success = true, Fulfillment = Prop(doc, "fulfillment") };
        }));

    [Display(Name = ShopifyMethods.ListCustomers)]
    [Description("List customers.")]
    [Parameters(typeof(ListCustomersArgs))]
    public Task<object> ListCustomers(ServiceConfig config, ListCustomersArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/customers.json?limit={r.Limit ?? 50}");
            return new { Success = true, Customers = Prop(doc, "customers") };
        });

    [Display(Name = ShopifyMethods.SearchCustomers)]
    [Description("Search customers by name, email or other fields.")]
    [Parameters(typeof(SearchCustomersArgs))]
    public Task<object> SearchCustomers(ServiceConfig config, SearchCustomersArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/customers/search.json?query={Uri.EscapeDataString(r.Query)}");
            return new { Success = true, Customers = Prop(doc, "customers") };
        });

    [Display(Name = ShopifyMethods.CreateDraftOrder)]
    [Description("Create a draft order with a single custom line item, optionally tied to a customer email.")]
    [Parameters(typeof(CreateDraftOrderArgs))]
    public Task<object> CreateDraftOrder(ServiceConfig config, CreateDraftOrderArgs r) =>
        Guard(() => Confirm(config, r, "Create this draft order?", $"{r.Title} @ {r.Price}", async () =>
        {
            using var client = Client(config);
            var draft = new Dictionary<string, object>
            {
                ["line_items"] = new[]
                {
                    new { title = r.Title, price = (r.Price ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture), quantity = 1 }
                }
            };
            if (!string.IsNullOrWhiteSpace(r.Email)) draft["email"] = r.Email;
            var doc = await client.PostAsync("/draft_orders.json", new { draft_order = draft });
            return new { Success = true, DraftOrder = Prop(doc, "draft_order") };
        }));

    // ───────────────────────────── Plumbing ─────────────────────────────

    private static object Prop(JsonElement doc, string name) =>
        doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty(name, out var el) ? el : doc;

    private async Task<object> Confirm(ServiceConfig config, IPluginServiceRequest request, string message, string content, Func<Task<object>> execute)
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
            await _logger(LogLevel.Error, $"Shopify operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
