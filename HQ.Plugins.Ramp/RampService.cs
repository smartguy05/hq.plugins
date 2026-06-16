using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Ramp.Models;

namespace HQ.Plugins.Ramp;

/// <summary>Tool surface for Ramp spend management (transactions, cards, reimbursements, users). Read-only.</summary>
public class RampService
{
    private readonly LogDelegate _logger;

    public RampService(LogDelegate logger) => _logger = logger;

    [Display(Name = RampMethods.ListTransactions)]
    [Description("List card transactions, optionally filtered by date range.")]
    [Parameters("""{"type":"object","properties":{"fromDate":{"type":"string","description":"ISO date/time lower bound"},"toDate":{"type":"string","description":"ISO date/time upper bound"},"pageSize":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> ListTransactions(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var qs = new List<string> { $"page_size={r.PageSize ?? 50}" };
            if (!string.IsNullOrWhiteSpace(r.FromDate)) qs.Add($"from_date={Uri.EscapeDataString(r.FromDate)}");
            if (!string.IsNullOrWhiteSpace(r.ToDate)) qs.Add($"to_date={Uri.EscapeDataString(r.ToDate)}");
            var doc = await client.GetAsync($"/transactions?{string.Join("&", qs)}");
            return new { Success = true, Transactions = Prop(doc, "data") };
        });

    [Display(Name = RampMethods.GetTransaction)]
    [Description("Get a single transaction by ID.")]
    [Parameters("""{"type":"object","properties":{"transactionId":{"type":"string"}},"required":["transactionId"]}""")]
    public Task<object> GetTransaction(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/transactions/{r.TransactionId}");
            return new { Success = true, Transaction = doc };
        });

    [Display(Name = RampMethods.ListCards)]
    [Description("List issued cards.")]
    [Parameters("""{"type":"object","properties":{"pageSize":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> ListCards(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/cards?page_size={r.PageSize ?? 50}");
            return new { Success = true, Cards = Prop(doc, "data") };
        });

    [Display(Name = RampMethods.GetCard)]
    [Description("Get a single card by ID.")]
    [Parameters("""{"type":"object","properties":{"cardId":{"type":"string"}},"required":["cardId"]}""")]
    public Task<object> GetCard(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/cards/{r.CardId}");
            return new { Success = true, Card = doc };
        });

    [Display(Name = RampMethods.ListReimbursements)]
    [Description("List employee reimbursements.")]
    [Parameters("""{"type":"object","properties":{"pageSize":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> ListReimbursements(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/reimbursements?page_size={r.PageSize ?? 50}");
            return new { Success = true, Reimbursements = Prop(doc, "data") };
        });

    [Display(Name = RampMethods.ListUsers)]
    [Description("List users (employees) on the Ramp account.")]
    [Parameters("""{"type":"object","properties":{"pageSize":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> ListUsers(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/users?page_size={r.PageSize ?? 50}");
            return new { Success = true, Users = Prop(doc, "data") };
        });

    [Display(Name = RampMethods.ListDepartments)]
    [Description("List departments.")]
    [Parameters("""{"type":"object","properties":{"pageSize":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> ListDepartments(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/departments?page_size={r.PageSize ?? 50}");
            return new { Success = true, Departments = Prop(doc, "data") };
        });

    [Display(Name = RampMethods.GetSpendLimits)]
    [Description("List spend limits configured on the account.")]
    [Parameters("""{"type":"object","properties":{"pageSize":{"type":"integer","description":"Max results (default 50)"}},"required":[]}""")]
    public Task<object> GetSpendLimits(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/limits?page_size={r.PageSize ?? 50}");
            return new { Success = true, Limits = Prop(doc, "data") };
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
            await _logger(LogLevel.Error, $"Ramp operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
