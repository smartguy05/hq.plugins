using System.Text.Json;
using System.Text.Json.Serialization;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Gusto.Models;

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

    // CompanyId is optional — resolved from /v1/me when omitted.
    [JsonConverter(typeof(StringOrNumberConverter))] public string CompanyId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string EmployeeId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))] public string PayrollId { get; set; }

    // Time-off request
    public string StartDate { get; set; }   // YYYY-MM-DD
    public string EndDate { get; set; }
    public double? Hours { get; set; }
    public string RequestType { get; set; } // vacation | sick
}
