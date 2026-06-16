using System.Text.Json;
using System.Text.Json.Serialization;
using HQ.Models.Interfaces;

namespace HQ.Plugins.GoogleWorkspace.Models;

/// <summary>
/// Handles JSON values that may arrive as either strings or numbers (LLMs often send
/// numeric IDs without quotes). Converts both to a C# string.
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

    // ── File / document identity ──
    // FileId doubles as Drive fileId, Docs documentId, and Sheets spreadsheetId.
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string FileId { get; set; }

    [JsonConverter(typeof(StringOrNumberConverter))]
    public string FolderId { get; set; }

    public string Name { get; set; }
    public string Title { get; set; }
    public string MimeType { get; set; }

    // Base64-encoded file bytes for uploads.
    public string Content { get; set; }

    // ── Drive listing / search ──
    public string Query { get; set; }
    public int? PageSize { get; set; }
    public string OrderBy { get; set; }

    // ── Drive delete / share ──
    public bool? Permanent { get; set; }
    public string Role { get; set; }        // reader | writer | commenter | owner
    public string Type { get; set; }        // user | group | domain | anyone
    public string EmailAddress { get; set; }

    // ── Docs ──
    public string Text { get; set; }
    public string Find { get; set; }
    public string Replace { get; set; }
    public bool? MatchCase { get; set; }

    // ── Sheets ──
    public string Range { get; set; }
    public string SheetTitle { get; set; }

    // 2D array of cell values. Leaf cells arrive as JsonElement and are normalized
    // to string/double/bool by SheetsClient before being sent to the Sheets API.
    public List<List<JsonElement>> Values { get; set; }
}
