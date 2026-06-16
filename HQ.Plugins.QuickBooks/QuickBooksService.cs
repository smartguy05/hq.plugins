using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.QuickBooks.Models;

namespace HQ.Plugins.QuickBooks;

/// <summary>
/// Tool surface for QuickBooks Online (invoices, customers, expenses, reports). Write actions
/// route through the HQ confirmation flow when config.RequiresConfirmation is set. v1 scope is
/// deliberately tight; payroll, journal entries and transaction recategorization are deferred.
/// </summary>
public class QuickBooksService
{
    private const string PluginName = "QuickBooks";
    private readonly LogDelegate _logger;
    private readonly INotificationService _notificationService;

    public QuickBooksService(INotificationService notificationService, LogDelegate logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    // ───────────────────────────── Customers ─────────────────────────────

    [Display(Name = QuickBooksMethods.ListCustomers)]
    [Description("List customers.")]
    [Parameters("""{"type":"object","properties":{"limit":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> ListCustomers(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Query(config, "Customer", r.Limit ?? 50));

    [Display(Name = QuickBooksMethods.CreateCustomer)]
    [Description("Create a new customer.")]
    [Parameters("""{"type":"object","properties":{"displayName":{"type":"string","description":"Customer display name (required, must be unique)"},"email":{"type":"string"},"companyName":{"type":"string"}},"required":["displayName"]}""")]
    public Task<object> CreateCustomer(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Confirm(config, r, "Create this customer?", r.DisplayName, async () =>
        {
            using var client = new QuickBooksClient(config);
            var body = new Dictionary<string, object> { ["DisplayName"] = r.DisplayName };
            if (!string.IsNullOrWhiteSpace(r.CompanyName)) body["CompanyName"] = r.CompanyName;
            if (!string.IsNullOrWhiteSpace(r.Email)) body["PrimaryEmailAddr"] = new { Address = r.Email };
            var doc = await client.PostAsync("/customer", body);
            return new { Success = true, Customer = Prop(doc, "Customer") };
        }));

    // ───────────────────────────── Invoices ─────────────────────────────

    [Display(Name = QuickBooksMethods.CreateInvoice)]
    [Description("Create an invoice for a customer with a single line item. itemId defaults to '1' (the sandbox 'Services' item) — use list-able items in production.")]
    [Parameters("""{"type":"object","properties":{"customerId":{"type":"string","description":"Customer ID"},"amount":{"type":"number","description":"Line amount"},"itemId":{"type":"string","description":"Sales item ID (default '1')"},"description":{"type":"string"}},"required":["customerId","amount"]}""")]
    public Task<object> CreateInvoice(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Confirm(config, r, "Create this invoice?", $"Customer {r.CustomerId}, amount {r.Amount}", async () =>
        {
            using var client = new QuickBooksClient(config);
            var body = new
            {
                CustomerRef = new { value = r.CustomerId },
                Line = new[]
                {
                    new
                    {
                        Amount = r.Amount,
                        DetailType = "SalesItemLineDetail",
                        Description = r.Description,
                        SalesItemLineDetail = new { ItemRef = new { value = string.IsNullOrWhiteSpace(r.ItemId) ? "1" : r.ItemId } }
                    }
                }
            };
            var doc = await client.PostAsync("/invoice", body);
            return new { Success = true, Invoice = Prop(doc, "Invoice") };
        }));

    [Display(Name = QuickBooksMethods.SendInvoice)]
    [Description("Email an existing invoice to the customer.")]
    [Parameters("""{"type":"object","properties":{"invoiceId":{"type":"string"},"sendTo":{"type":"string","description":"Override recipient email (optional)"}},"required":["invoiceId"]}""")]
    public Task<object> SendInvoice(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Confirm(config, r, "Email this invoice?", $"Invoice {r.InvoiceId}", async () =>
        {
            using var client = new QuickBooksClient(config);
            var path = string.IsNullOrWhiteSpace(r.SendTo)
                ? $"/invoice/{r.InvoiceId}/send"
                : $"/invoice/{r.InvoiceId}/send?sendTo={Uri.EscapeDataString(r.SendTo)}";
            var doc = await client.PostAsync(path, new { });
            return new { Success = true, Invoice = Prop(doc, "Invoice") };
        }));

    [Display(Name = QuickBooksMethods.ListInvoices)]
    [Description("List invoices.")]
    [Parameters("""{"type":"object","properties":{"limit":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> ListInvoices(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Query(config, "Invoice", r.Limit ?? 50));

    // ───────────────────────────── Expenses / bills ─────────────────────────────

    [Display(Name = QuickBooksMethods.ListExpenses)]
    [Description("List expenses (purchases).")]
    [Parameters("""{"type":"object","properties":{"limit":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> ListExpenses(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Query(config, "Purchase", r.Limit ?? 50));

    [Display(Name = QuickBooksMethods.CreateExpense)]
    [Description("Record an expense (purchase) paid from an account against an expense category account.")]
    [Parameters("""{"type":"object","properties":{"paymentAccountId":{"type":"string","description":"Account the money was paid FROM (bank/credit card)"},"expenseAccountId":{"type":"string","description":"Expense category account ID"},"amount":{"type":"number"},"paymentType":{"type":"string","description":"Cash | Check | CreditCard"},"vendorId":{"type":"string","description":"Vendor/payee ID (optional)"},"description":{"type":"string"}},"required":["paymentAccountId","expenseAccountId","amount"]}""")]
    public Task<object> CreateExpense(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Confirm(config, r, "Record this expense?", $"{r.Amount} from account {r.PaymentAccountId}", async () =>
        {
            using var client = new QuickBooksClient(config);
            var body = new Dictionary<string, object>
            {
                ["AccountRef"] = new { value = r.PaymentAccountId },
                ["PaymentType"] = string.IsNullOrWhiteSpace(r.PaymentType) ? "Cash" : r.PaymentType,
                ["Line"] = new[]
                {
                    new
                    {
                        Amount = r.Amount,
                        DetailType = "AccountBasedExpenseLineDetail",
                        Description = r.Description,
                        AccountBasedExpenseLineDetail = new { AccountRef = new { value = r.ExpenseAccountId } }
                    }
                }
            };
            if (!string.IsNullOrWhiteSpace(r.VendorId))
                body["EntityRef"] = new { value = r.VendorId, type = "Vendor" };
            var doc = await client.PostAsync("/purchase", body);
            return new { Success = true, Purchase = Prop(doc, "Purchase") };
        }));

    [Display(Name = QuickBooksMethods.CreateBill)]
    [Description("Create a bill (accounts payable) owed to a vendor.")]
    [Parameters("""{"type":"object","properties":{"vendorId":{"type":"string"},"expenseAccountId":{"type":"string","description":"Expense category account ID"},"amount":{"type":"number"},"description":{"type":"string"}},"required":["vendorId","expenseAccountId","amount"]}""")]
    public Task<object> CreateBill(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Confirm(config, r, "Create this bill?", $"Vendor {r.VendorId}, amount {r.Amount}", async () =>
        {
            using var client = new QuickBooksClient(config);
            var body = new
            {
                VendorRef = new { value = r.VendorId },
                Line = new[]
                {
                    new
                    {
                        Amount = r.Amount,
                        DetailType = "AccountBasedExpenseLineDetail",
                        Description = r.Description,
                        AccountBasedExpenseLineDetail = new { AccountRef = new { value = r.ExpenseAccountId } }
                    }
                }
            };
            var doc = await client.PostAsync("/bill", body);
            return new { Success = true, Bill = Prop(doc, "Bill") };
        }));

    // ───────────────────────────── Reference data / reports ─────────────────────────────

    [Display(Name = QuickBooksMethods.ListAccounts)]
    [Description("List accounts from the chart of accounts.")]
    [Parameters("""{"type":"object","properties":{"limit":{"type":"integer","description":"Max results (default 100)"}},"required":[]}""")]
    public Task<object> ListAccounts(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Query(config, "Account", r.Limit ?? 100));

    [Display(Name = QuickBooksMethods.ListVendors)]
    [Description("List vendors.")]
    [Parameters("""{"type":"object","properties":{"limit":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> ListVendors(ServiceConfig config, ServiceRequest r) =>
        Guard(() => Query(config, "Vendor", r.Limit ?? 50));

    [Display(Name = QuickBooksMethods.RunReport)]
    [Description("Run a financial report. reportName e.g. ProfitAndLoss, BalanceSheet, AgedReceivables.")]
    [Parameters("""{"type":"object","properties":{"reportName":{"type":"string","description":"ProfitAndLoss | BalanceSheet | AgedReceivables | etc."},"startDate":{"type":"string","description":"YYYY-MM-DD"},"endDate":{"type":"string","description":"YYYY-MM-DD"}},"required":["reportName"]}""")]
    public Task<object> RunReport(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new QuickBooksClient(config);
            var path = $"/reports/{Uri.EscapeDataString(r.ReportName)}";
            var qs = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.StartDate)) qs.Add($"start_date={r.StartDate}");
            if (!string.IsNullOrWhiteSpace(r.EndDate)) qs.Add($"end_date={r.EndDate}");
            if (qs.Count > 0) path += "?" + string.Join("&", qs);
            var doc = await client.GetAsync(path);
            return new { Success = true, Report = doc };
        });

    // ───────────────────────────── Plumbing ─────────────────────────────

    private static async Task<object> Query(ServiceConfig config, string entity, int limit)
    {
        using var client = new QuickBooksClient(config);
        var doc = await client.QueryAsync($"SELECT * FROM {entity} MAXRESULTS {limit}");
        var items = doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("QueryResponse", out var qr) && qr.TryGetProperty(entity, out var arr)
            ? (object)arr
            : Array.Empty<object>();
        return new { Success = true, Items = items };
    }

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
            await _logger(LogLevel.Error, $"QuickBooks operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
