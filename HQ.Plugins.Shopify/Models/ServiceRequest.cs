using System.Text.Json;
using System.Text.Json.Serialization;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Shopify.Models;

/// <summary>Handles JSON values that may arrive as either strings or numbers (Shopify IDs).</summary>
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

    [JsonConverter(typeof(StringOrNumberConverter))] public string ProductId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string OrderId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string CustomerId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string InventoryItemId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string LocationId { get; set; }

    // Product
    public string Title { get; set; }
    public string BodyHtml { get; set; }
    public string Vendor { get; set; }
    public string ProductType { get; set; }
    public decimal? Price { get; set; }

    // Inventory
    public int? Available { get; set; }

    // Customers / draft order
    public string Query { get; set; }
    public string Email { get; set; }

    // Paging
    public int? Limit { get; set; }
}
