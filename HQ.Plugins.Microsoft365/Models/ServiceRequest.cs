using System.Text.Json;
using System.Text.Json.Serialization;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Microsoft365.Models;

/// <summary>
/// Handles JSON values that may arrive as either strings or numbers.
/// </summary>
public class StringOrNumberConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt64().ToString(),
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => reader.GetString()
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // ── Drive / item identity ──
    // DriveId is optional per-request; falls back to ServiceConfig.DefaultDriveId.
    public string DriveId { get; set; }

    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    // Alternatively address an item by path relative to the drive root, e.g. "Reports/Q3.xlsx".
    public string Path { get; set; }

    public string Name { get; set; }
    public string Content { get; set; }   // base64 for uploads
    public string MimeType { get; set; }

    // ── Listing / search ──
    public string Query { get; set; }
    public int? PageSize { get; set; }

    // ── Move / copy ──
    public string DestinationFolderId { get; set; }

    // ── Share ──
    public string LinkType { get; set; }   // view | edit
    public string Scope { get; set; }      // anonymous | organization

    // ── Word create ──
    public string Text { get; set; }

    // ── Excel ──
    public string Worksheet { get; set; }
    public string Range { get; set; }      // A1 address, e.g. "A1:C5"
    public string WorksheetName { get; set; }
    public List<List<JsonElement>> Values { get; set; }
}
