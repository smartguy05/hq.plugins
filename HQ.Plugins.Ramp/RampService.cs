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
    [Parameters(typeof(ListTransactionsArgs))]
    public Task<object> ListTransactions(ServiceConfig config, ListTransactionsArgs request) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var qs = new List<string> { $"page_size={request.PageSize ?? 50}" };
            if (!string.IsNullOrWhiteSpace(request.FromDate)) qs.Add($"from_date={Uri.EscapeDataString(request.FromDate)}");
            if (!string.IsNullOrWhiteSpace(request.ToDate)) qs.Add($"to_date={Uri.EscapeDataString(request.ToDate)}");
            var doc = await client.GetAsync($"/transactions?{string.Join("&", qs)}");
            return new { Success = true, Transactions = Prop(doc, "data") };
        });

    [Display(Name = RampMethods.GetTransaction)]
    [Description("Get a single transaction by ID.")]
    [Parameters(typeof(GetTransactionArgs))]
    public Task<object> GetTransaction(ServiceConfig config, GetTransactionArgs request) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/transactions/{request.TransactionId}");
            return new { Success = true, Transaction = doc };
        });

    [Display(Name = RampMethods.ListCards)]
    [Description("List issued cards.")]
    [Parameters(typeof(ListCardsArgs))]
    public Task<object> ListCards(ServiceConfig config, ListCardsArgs request) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/cards?page_size={request.PageSize ?? 50}");
            return new { Success = true, Cards = Prop(doc, "data") };
        });

    [Display(Name = RampMethods.GetCard)]
    [Description("Get a single card by ID.")]
    [Parameters(typeof(GetCardArgs))]
    public Task<object> GetCard(ServiceConfig config, GetCardArgs request) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/cards/{request.CardId}");
            return new { Success = true, Card = doc };
        });

    [Display(Name = RampMethods.ListReimbursements)]
    [Description("List employee reimbursements.")]
    [Parameters(typeof(ListReimbursementsArgs))]
    public Task<object> ListReimbursements(ServiceConfig config, ListReimbursementsArgs request) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/reimbursements?page_size={request.PageSize ?? 50}");
            return new { Success = true, Reimbursements = Prop(doc, "data") };
        });

    [Display(Name = RampMethods.ListUsers)]
    [Description("List users (employees) on the Ramp account.")]
    [Parameters(typeof(ListUsersArgs))]
    public Task<object> ListUsers(ServiceConfig config, ListUsersArgs request) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/users?page_size={request.PageSize ?? 50}");
            return new { Success = true, Users = Prop(doc, "data") };
        });

    [Display(Name = RampMethods.ListDepartments)]
    [Description("List departments.")]
    [Parameters(typeof(ListDepartmentsArgs))]
    public Task<object> ListDepartments(ServiceConfig config, ListDepartmentsArgs request) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/departments?page_size={request.PageSize ?? 50}");
            return new { Success = true, Departments = Prop(doc, "data") };
        });

    [Display(Name = RampMethods.GetSpendLimits)]
    [Description("List spend limits configured on the account.")]
    [Parameters(typeof(GetSpendLimitsArgs))]
    public Task<object> GetSpendLimits(ServiceConfig config, GetSpendLimitsArgs request) =>
        Guard(async () =>
        {
            using var client = new RampClient(config);
            var doc = await client.GetAsync($"/limits?page_size={request.PageSize ?? 50}");
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
