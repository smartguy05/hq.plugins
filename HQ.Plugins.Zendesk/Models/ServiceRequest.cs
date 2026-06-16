using System.Text.Json;
using System.Text.Json.Serialization;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Zendesk.Models;

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
    [JsonConverter(typeof(StringOrNumberConverter))] public string TicketId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string UserId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string MacroId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string RequesterId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string AssigneeId { get; set; }

    // Ticket fields
    public string Subject { get; set; }
    public string Comment { get; set; }      // comment body
    public bool? Public { get; set; }         // public (customer-facing) vs internal note
    public string Status { get; set; }        // new | open | pending | hold | solved | closed
    public string Priority { get; set; }      // low | normal | high | urgent
    public string Tags { get; set; }          // comma-separated

    // Search / paging
    public string Query { get; set; }
    public int? PageSize { get; set; }
}
