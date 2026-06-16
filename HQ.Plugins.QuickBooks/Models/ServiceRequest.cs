using System.Text.Json;
using System.Text.Json.Serialization;
using HQ.Models.Interfaces;

namespace HQ.Plugins.QuickBooks.Models;

/// <summary>Handles JSON values that may arrive as either strings or numbers (QBO uses string IDs).</summary>
public class StringOrNumberConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt64().ToString(),
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => reader.GetString()
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Entity IDs (QBO IDs are strings, but LLMs may send numbers)
    [JsonConverter(typeof(StringOrNumberConverter))] public string CustomerId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string InvoiceId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string PurchaseId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string VendorId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string ItemId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string ExpenseAccountId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string PaymentAccountId { get; set; }

    // Customer / vendor fields
    public string DisplayName { get; set; }
    public string Email { get; set; }
    public string CompanyName { get; set; }

    // Money
    public decimal? Amount { get; set; }
    public string Description { get; set; }
    public string PaymentType { get; set; }   // Cash | Check | CreditCard

    // Send invoice
    public string SendTo { get; set; }         // override recipient email

    // Reports
    public string ReportName { get; set; }     // ProfitAndLoss | BalanceSheet | AgedReceivables
    public string StartDate { get; set; }      // YYYY-MM-DD
    public string EndDate { get; set; }

    // Paging
    public int? Limit { get; set; }
}
