using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Stripe.Models;
using Stripe;

namespace HQ.Plugins.Stripe;

/// <summary>
/// Tool surface for Stripe payments &amp; invoicing. Money-moving tools route through the
/// HQ confirmation flow (see EmailService) when config.RequiresConfirmation is set.
/// </summary>
public class StripeService
{
    private const string PluginName = "Stripe";
    private readonly LogDelegate _logger;
    private readonly INotificationService _notificationService;

    public StripeService(INotificationService notificationService, LogDelegate logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    private static RequestOptions Ro(ServiceConfig config) => new() { ApiKey = config.ApiKey };

    // ───────────────────────────── Invoices ─────────────────────────────

    [Display(Name = StripeMethods.CreateInvoice)]
    [Description("Create a draft invoice for a customer. If amount+currency are supplied, a single line item is added.")]
    [Parameters("""{"type":"object","properties":{"customerId":{"type":"string","description":"Stripe customer ID"},"amount":{"type":"integer","description":"Line item amount in the smallest currency unit (cents)"},"currency":{"type":"string","description":"3-letter ISO currency, e.g. 'usd'"},"description":{"type":"string","description":"Line item / invoice description"}},"required":["customerId"]}""")]
    [SupportsConfirmation]
    public Task<object> CreateInvoice(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Confirm(config, r, "Create this invoice?", $"Customer {r.CustomerId}, {r.Amount} {r.Currency}", async () =>
        {
            if (r.Amount.HasValue)
            {
                await new InvoiceItemService().CreateAsync(new InvoiceItemCreateOptions
                {
                    Customer = r.CustomerId,
                    Amount = r.Amount,
                    Currency = string.IsNullOrWhiteSpace(r.Currency) ? "usd" : r.Currency,
                    Description = r.Description
                }, Ro(config));
            }

            var invoice = await new InvoiceService().CreateAsync(new InvoiceCreateOptions
            {
                Customer = r.CustomerId,
                Description = r.Description,
                AutoAdvance = false
            }, Ro(config));

            return new { Success = true, InvoiceId = invoice.Id, invoice.Status, invoice.Total, invoice.Currency, invoice.HostedInvoiceUrl };
        }));

    [Display(Name = StripeMethods.SendInvoice)]
    [Description("Finalize and email an existing invoice to the customer.")]
    [Parameters("""{"type":"object","properties":{"invoiceId":{"type":"string","description":"The invoice ID to send"}},"required":["invoiceId"]}""")]
    [SupportsConfirmation]
    public Task<object> SendInvoice(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Confirm(config, r, "Send this invoice to the customer?", $"Invoice {r.InvoiceId}", async () =>
        {
            var invoice = await new InvoiceService().SendInvoiceAsync(r.InvoiceId, null, Ro(config));
            return new { Success = true, InvoiceId = invoice.Id, invoice.Status, invoice.HostedInvoiceUrl };
        }));

    [Display(Name = StripeMethods.ListInvoices)]
    [Description("List recent invoices, optionally filtered to a customer.")]
    [Parameters("""{"type":"object","properties":{"customerId":{"type":"string","description":"Filter to this customer"},"limit":{"type":"integer","description":"Max results (default 20)"}},"required":[]}""")]
    public Task<object> ListInvoices(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            var options = new InvoiceListOptions { Limit = r.Limit ?? 20 };
            if (!string.IsNullOrWhiteSpace(r.CustomerId)) options.Customer = r.CustomerId;
            var list = await new InvoiceService().ListAsync(options, Ro(config));
            return new { Success = true, Invoices = list.Select(i => new { i.Id, i.Status, i.Total, i.Currency, i.CustomerId, i.HostedInvoiceUrl }) };
        });

    // ───────────────────────────── Payment links ─────────────────────────────

    [Display(Name = StripeMethods.CreatePaymentLink)]
    [Description("Create a shareable payment link. Either reference an existing priceId, or supply amount+currency+productName to create one.")]
    [Parameters("""{"type":"object","properties":{"priceId":{"type":"string","description":"Existing Stripe price ID"},"amount":{"type":"integer","description":"Amount in cents (if creating a new price)"},"currency":{"type":"string","description":"3-letter ISO currency"},"productName":{"type":"string","description":"Product name (if creating a new price)"},"quantity":{"type":"integer","description":"Quantity (default 1)"}},"required":[]}""")]
    [SupportsConfirmation]
    public Task<object> CreatePaymentLink(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Confirm(config, r, "Create this payment link?", $"{r.Amount} {r.Currency} {r.ProductName}".Trim(), async () =>
        {
            var priceId = r.PriceId;
            if (string.IsNullOrWhiteSpace(priceId))
            {
                if (!r.Amount.HasValue) return new { Success = false, Error = "Provide priceId, or amount+currency+productName." };
                var price = await new PriceService().CreateAsync(new PriceCreateOptions
                {
                    UnitAmount = r.Amount,
                    Currency = string.IsNullOrWhiteSpace(r.Currency) ? "usd" : r.Currency,
                    ProductData = new PriceProductDataOptions { Name = string.IsNullOrWhiteSpace(r.ProductName) ? "Payment" : r.ProductName }
                }, Ro(config));
                priceId = price.Id;
            }

            var link = await new PaymentLinkService().CreateAsync(new PaymentLinkCreateOptions
            {
                LineItems = [new PaymentLinkLineItemOptions { Price = priceId, Quantity = r.Quantity ?? 1 }]
            }, Ro(config));
            return new { Success = true, PaymentLinkId = link.Id, link.Url, link.Active };
        }));

    // ───────────────────────────── Customers ─────────────────────────────

    [Display(Name = StripeMethods.GetCustomer)]
    [Description("Retrieve a Stripe customer by ID.")]
    [Parameters("""{"type":"object","properties":{"customerId":{"type":"string","description":"The customer ID"}},"required":["customerId"]}""")]
    public Task<object> GetCustomer(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            var c = await new CustomerService().GetAsync(r.CustomerId, null, Ro(config));
            return new { Success = true, Customer = new { c.Id, c.Email, c.Name, c.Description, c.Balance, c.Currency } };
        });

    [Display(Name = StripeMethods.SearchCustomers)]
    [Description("Search customers using Stripe search query syntax, e.g. \"email:'jane@acme.com'\" or \"name:'Acme'\".")]
    [Parameters("""{"type":"object","properties":{"query":{"type":"string","description":"Stripe search query"},"limit":{"type":"integer","description":"Max results (default 20)"}},"required":["query"]}""")]
    public Task<object> SearchCustomers(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            var result = await new CustomerService().SearchAsync(new CustomerSearchOptions { Query = r.Query, Limit = r.Limit ?? 20 }, Ro(config));
            return new { Success = true, Customers = result.Select(c => new { c.Id, c.Email, c.Name, c.Description }) };
        });

    [Display(Name = StripeMethods.CreateCustomer)]
    [Description("Create a new Stripe customer.")]
    [Parameters("""{"type":"object","properties":{"email":{"type":"string"},"name":{"type":"string"},"description":{"type":"string"}},"required":["email"]}""")]
    public Task<object> CreateCustomer(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            var c = await new CustomerService().CreateAsync(new CustomerCreateOptions { Email = r.Email, Name = r.Name, Description = r.Description }, Ro(config));
            return new { Success = true, Customer = new { c.Id, c.Email, c.Name } };
        });

    // ───────────────────────────── Payments / refunds / balance ─────────────────────────────

    [Display(Name = StripeMethods.ListPayments)]
    [Description("List recent payments (PaymentIntents), optionally filtered to a customer.")]
    [Parameters("""{"type":"object","properties":{"customerId":{"type":"string","description":"Filter to this customer"},"limit":{"type":"integer","description":"Max results (default 20)"}},"required":[]}""")]
    public Task<object> ListPayments(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            var options = new PaymentIntentListOptions { Limit = r.Limit ?? 20 };
            if (!string.IsNullOrWhiteSpace(r.CustomerId)) options.Customer = r.CustomerId;
            var list = await new PaymentIntentService().ListAsync(options, Ro(config));
            return new { Success = true, Payments = list.Select(p => new { p.Id, p.Amount, p.Currency, p.Status, p.CustomerId, p.Description }) };
        });

    [Display(Name = StripeMethods.CreateRefund)]
    [Description("Refund a payment by PaymentIntent or Charge ID. Omit amount for a full refund.")]
    [Parameters("""{"type":"object","properties":{"paymentIntentId":{"type":"string","description":"PaymentIntent to refund"},"chargeId":{"type":"string","description":"Charge to refund (alternative to paymentIntentId)"},"amount":{"type":"integer","description":"Partial refund amount in cents (omit for full refund)"}},"required":[]}""")]
    [SupportsConfirmation]
    public Task<object> CreateRefund(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Confirm(config, r, "Issue this refund?", $"{r.PaymentIntentId ?? r.ChargeId} amount {(r.Amount?.ToString() ?? "full")}", async () =>
        {
            if (string.IsNullOrWhiteSpace(r.PaymentIntentId) && string.IsNullOrWhiteSpace(r.ChargeId))
                return new { Success = false, Error = "Provide paymentIntentId or chargeId." };
            var options = new RefundCreateOptions { Amount = r.Amount };
            if (!string.IsNullOrWhiteSpace(r.PaymentIntentId)) options.PaymentIntent = r.PaymentIntentId;
            else options.Charge = r.ChargeId;
            var refund = await new RefundService().CreateAsync(options, Ro(config));
            return new { Success = true, RefundId = refund.Id, refund.Amount, refund.Currency, refund.Status };
        }));

    [Display(Name = StripeMethods.GetBalance)]
    [Description("Get the current Stripe account balance (available and pending).")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public Task<object> GetBalance(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            var balance = await new BalanceService().GetAsync(null, Ro(config));
            return new
            {
                Success = true,
                Available = balance.Available?.Select(a => new { a.Amount, a.Currency }),
                Pending = balance.Pending?.Select(a => new { a.Amount, a.Currency })
            };
        });

    // ───────────────────────────── Plumbing ─────────────────────────────

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
        catch (StripeException ex)
        {
            await _logger(LogLevel.Error, $"Stripe API error: {ex.StripeError?.Message ?? ex.Message}", ex);
            return new { Success = false, Error = ex.StripeError?.Message ?? ex.Message };
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Stripe operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
