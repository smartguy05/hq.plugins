using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Plaid.Models;

namespace HQ.Plugins.Plaid;

/// <summary>
/// Read-only Plaid tools. The per-item access_token comes from the Plaid Link setup flow (handled by
/// the host); these tools just read with the stored token. The token is long-lived (no refresh).
/// </summary>
public class PlaidService
{
    private readonly LogDelegate _logger;

    public PlaidService(LogDelegate logger) => _logger = logger;

    /// <summary>Resolve the Plaid API base URL for an environment string. Defaults to sandbox.</summary>
    public static string BaseUrlFor(string environment) =>
        (environment ?? "").Trim().ToLowerInvariant() switch
        {
            "production" or "prod" => "https://production.plaid.com",
            _ => "https://sandbox.plaid.com"
        };

    /// <summary>Format a date for Plaid (YYYY-MM-DD), falling back to a default.</summary>
    public static string Date(string value, DateTime fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback.ToString("yyyy-MM-dd") : value.Trim();

    private static string Token(ServiceConfig config, ServiceRequest r) =>
        !string.IsNullOrWhiteSpace(r.AccessToken) ? r.AccessToken : config.AccessToken;

    [Display(Name = PlaidMethods.ListAccounts)]
    [Description("List the bank accounts linked to the connected item (names, types, masks).")]
    [Parameters("""{"type":"object","properties":{"accessToken":{"type":"string","description":"Optional item access_token override"}},"required":[]}""")]
    public Task<object> ListAccounts(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.PostAsync("/accounts/get", new JsonObject { ["access_token"] = Token(config, r) });
            return new { Success = true, Accounts = Prop(doc, "accounts"), Item = Prop(doc, "item") };
        });

    [Display(Name = PlaidMethods.GetBalances)]
    [Description("Get real-time balances for the connected item's accounts.")]
    [Parameters("""{"type":"object","properties":{"accessToken":{"type":"string","description":"Optional item access_token override"}},"required":[]}""")]
    public Task<object> GetBalances(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.PostAsync("/accounts/balance/get", new JsonObject { ["access_token"] = Token(config, r) });
            return new { Success = true, Accounts = Prop(doc, "accounts") };
        });

    [Display(Name = PlaidMethods.ListTransactions)]
    [Description("List transactions for the connected item over a date range (defaults to the last 30 days).")]
    [Parameters("""{"type":"object","properties":{"startDate":{"type":"string","description":"YYYY-MM-DD (default 30 days ago)"},"endDate":{"type":"string","description":"YYYY-MM-DD (default today)"},"count":{"type":"integer","description":"Max results (default 100, max 500)"},"offset":{"type":"integer","description":"Pagination offset"},"accessToken":{"type":"string","description":"Optional item access_token override"}},"required":[]}""")]
    public Task<object> ListTransactions(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var now = DateTime.UtcNow;
            var body = new JsonObject
            {
                ["access_token"] = Token(config, r),
                ["start_date"] = Date(r.StartDate, now.AddDays(-30)),
                ["end_date"] = Date(r.EndDate, now),
                ["options"] = new JsonObject
                {
                    ["count"] = Math.Clamp(r.Count ?? 100, 1, 500),
                    ["offset"] = Math.Max(0, r.Offset ?? 0)
                }
            };
            var doc = await client.PostAsync("/transactions/get", body);
            return new
            {
                Success = true,
                Transactions = Prop(doc, "transactions"),
                Total = doc.TryGetProperty("total_transactions", out var t) ? t.GetInt32() : (int?)null
            };
        });

    private static PlaidClient Client(ServiceConfig config) =>
        new(BaseUrlFor(config.Environment), config.ClientId, config.Secret);

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
            await _logger(LogLevel.Error, $"Plaid operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
