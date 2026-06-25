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
    [Parameters(typeof(ListCustomersArgs))]
    public Task<object> ListCustomers(ServiceConfig config, ListCustomersArgs request) =>
        Guard(() => Query(config, "Customer", request.Limit ?? 50));

    [Display(Name = QuickBooksMethods.CreateCustomer)]
    [Description("Create a new customer.")]
    [Parameters(typeof(CreateCustomerArgs))]
    [SupportsConfirmation]
    public Task<object> CreateCustomer(ServiceConfig config, CreateCustomerArgs request) =>
        Guard(() => Confirm(config, request, "Create this customer?", request.DisplayName, async () =>
        {
            using var client = new QuickBooksClient(config);
            var body = new Dictionary<string, object> { ["DisplayName"] = request.DisplayName };
            if (!string.IsNullOrWhiteSpace(request.CompanyName)) body["CompanyName"] = request.CompanyName;
            if (!string.IsNullOrWhiteSpace(request.Email)) body["PrimaryEmailAddr"] = new { Address = request.Email };
            var doc = await client.PostAsync("/customer", body);
            return new { Success = true, Customer = Prop(doc, "Customer") };
        }));

    // ───────────────────────────── Invoices ─────────────────────────────

    [Display(Name = QuickBooksMethods.CreateInvoice)]
    [Description("Create an invoice for a customer with a single line item. itemId defaults to '1' (the sandbox 'Services' item) — use list-able items in production.")]
    [Parameters(typeof(CreateInvoiceArgs))]
    [SupportsConfirmation]
    public Task<object> CreateInvoice(ServiceConfig config, CreateInvoiceArgs request) =>
        Guard(() => Confirm(config, request, "Create this invoice?", $"Customer {request.CustomerId}, amount {request.Amount}", async () =>
        {
            using var client = new QuickBooksClient(config);
            var body = new
            {
                CustomerRef = new { value = request.CustomerId },
                Line = new[]
                {
                    new
                    {
                        Amount = request.Amount,
                        DetailType = "SalesItemLineDetail",
                        Description = request.Description,
                        SalesItemLineDetail = new { ItemRef = new { value = string.IsNullOrWhiteSpace(request.ItemId) ? "1" : request.ItemId } }
                    }
                }
            };
            var doc = await client.PostAsync("/invoice", body);
            return new { Success = true, Invoice = Prop(doc, "Invoice") };
        }));

    [Display(Name = QuickBooksMethods.SendInvoice)]
    [Description("Email an existing invoice to the customer.")]
    [Parameters(typeof(SendInvoiceArgs))]
    [SupportsConfirmation]
    public Task<object> SendInvoice(ServiceConfig config, SendInvoiceArgs request) =>
        Guard(() => Confirm(config, request, "Email this invoice?", $"Invoice {request.InvoiceId}", async () =>
        {
            using var client = new QuickBooksClient(config);
            var path = string.IsNullOrWhiteSpace(request.SendTo)
                ? $"/invoice/{request.InvoiceId}/send"
                : $"/invoice/{request.InvoiceId}/send?sendTo={Uri.EscapeDataString(request.SendTo)}";
            var doc = await client.PostAsync(path, new { });
            return new { Success = true, Invoice = Prop(doc, "Invoice") };
        }));

    [Display(Name = QuickBooksMethods.ListInvoices)]
    [Description("List invoices.")]
    [Parameters(typeof(ListInvoicesArgs))]
    public Task<object> ListInvoices(ServiceConfig config, ListInvoicesArgs request) =>
        Guard(() => Query(config, "Invoice", request.Limit ?? 50));

    // ───────────────────────────── Expenses / bills ─────────────────────────────

    [Display(Name = QuickBooksMethods.ListExpenses)]
    [Description("List expenses (purchases).")]
    [Parameters(typeof(ListExpensesArgs))]
    public Task<object> ListExpenses(ServiceConfig config, ListExpensesArgs request) =>
        Guard(() => Query(config, "Purchase", request.Limit ?? 50));

    [Display(Name = QuickBooksMethods.CreateExpense)]
    [Description("Record an expense (purchase) paid from an account against an expense category account.")]
    [Parameters(typeof(CreateExpenseArgs))]
    [SupportsConfirmation]
    public Task<object> CreateExpense(ServiceConfig config, CreateExpenseArgs request) =>
        Guard(() => Confirm(config, request, "Record this expense?", $"{request.Amount} from account {request.PaymentAccountId}", async () =>
        {
            using var client = new QuickBooksClient(config);
            var body = new Dictionary<string, object>
            {
                ["AccountRef"] = new { value = request.PaymentAccountId },
                ["PaymentType"] = string.IsNullOrWhiteSpace(request.PaymentType) ? "Cash" : request.PaymentType,
                ["Line"] = new[]
                {
                    new
                    {
                        Amount = request.Amount,
                        DetailType = "AccountBasedExpenseLineDetail",
                        Description = request.Description,
                        AccountBasedExpenseLineDetail = new { AccountRef = new { value = request.ExpenseAccountId } }
                    }
                }
            };
            if (!string.IsNullOrWhiteSpace(request.VendorId))
                body["EntityRef"] = new { value = request.VendorId, type = "Vendor" };
            var doc = await client.PostAsync("/purchase", body);
            return new { Success = true, Purchase = Prop(doc, "Purchase") };
        }));

    [Display(Name = QuickBooksMethods.CreateBill)]
    [Description("Create a bill (accounts payable) owed to a vendor.")]
    [Parameters(typeof(CreateBillArgs))]
    [SupportsConfirmation]
    public Task<object> CreateBill(ServiceConfig config, CreateBillArgs request) =>
        Guard(() => Confirm(config, request, "Create this bill?", $"Vendor {request.VendorId}, amount {request.Amount}", async () =>
        {
            using var client = new QuickBooksClient(config);
            var body = new
            {
                VendorRef = new { value = request.VendorId },
                Line = new[]
                {
                    new
                    {
                        Amount = request.Amount,
                        DetailType = "AccountBasedExpenseLineDetail",
                        Description = request.Description,
                        AccountBasedExpenseLineDetail = new { AccountRef = new { value = request.ExpenseAccountId } }
                    }
                }
            };
            var doc = await client.PostAsync("/bill", body);
            return new { Success = true, Bill = Prop(doc, "Bill") };
        }));

    // ───────────────────────────── Reference data / reports ─────────────────────────────

    [Display(Name = QuickBooksMethods.ListAccounts)]
    [Description("List accounts from the chart of accounts.")]
    [Parameters(typeof(ListAccountsArgs))]
    public Task<object> ListAccounts(ServiceConfig config, ListAccountsArgs request) =>
        Guard(() => Query(config, "Account", request.Limit ?? 100));

    [Display(Name = QuickBooksMethods.ListVendors)]
    [Description("List vendors.")]
    [Parameters(typeof(ListVendorsArgs))]
    public Task<object> ListVendors(ServiceConfig config, ListVendorsArgs request) =>
        Guard(() => Query(config, "Vendor", request.Limit ?? 50));

    [Display(Name = QuickBooksMethods.RunReport)]
    [Description("Run a financial report. reportName e.g. ProfitAndLoss, BalanceSheet, AgedReceivables.")]
    [Parameters(typeof(RunReportArgs))]
    public Task<object> RunReport(ServiceConfig config, RunReportArgs request) =>
        Guard(async () =>
        {
            using var client = new QuickBooksClient(config);
            var path = $"/reports/{Uri.EscapeDataString(request.ReportName)}";
            var qs = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.StartDate)) qs.Add($"start_date={request.StartDate}");
            if (!string.IsNullOrWhiteSpace(request.EndDate)) qs.Add($"end_date={request.EndDate}");
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
            await _logger(LogLevel.Error, $"QuickBooks operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
