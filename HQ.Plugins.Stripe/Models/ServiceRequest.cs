using System.Text.Json;
using System.Text.Json.Serialization;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Stripe.Models;

/// <summary>Handles JSON values that may arrive as either strings or numbers.</summary>
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

    // Identity
    [JsonConverter(typeof(StringOrNumberConverter))] public string CustomerId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string InvoiceId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string PaymentIntentId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string ChargeId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string PriceId { get; set; }

    // Customer fields
    public string Email { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    // Money — Amount is in the smallest currency unit (cents).
    public long? Amount { get; set; }
    public string Currency { get; set; }

    // Payment link / line item
    public string ProductName { get; set; }
    public long? Quantity { get; set; }

    // Search / paging
    public string Query { get; set; }
    public int? Limit { get; set; }
}
